using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public static class TextDataExtractor
{
    // 记录已处理文本，避免重复写入 CSV
    private static readonly HashSet<string> _processedStrings = new HashSet<string>();
    
    /// <summary>
    /// 将游戏内可提取文本导出到插件目录下的 `text` 文件夹。
    /// 流程：准备输出目录，清空去重集合，再分别导出场景、字符串表、物品、角色、
    /// 任务、日志、书籍和技能法术等多类文本。
    /// </summary>
    public static void ExtractAllTextToPluginDirectory()
    {
        // 获取插件目录与文本导出目录
        string pluginDirectory = GetPluginDirectory();
        string textDirectory = Path.Combine(pluginDirectory, "text");
        
        // 若目录不存在则创建
        if (!Directory.Exists(textDirectory))
        {
            Directory.CreateDirectory(textDirectory);
            Debug.Log($"Created text directory: {textDirectory}");
        }

        Debug.Log($"Extracting all text data to CSV files in: {textDirectory}");

        try
        {
        // 导出前清空已处理文本集合
        _processedStrings.Clear();
        
        // 按类型分别导出到独立 CSV 文件
            ExtractSceneDataToFile(textDirectory);
            ExtractStringListsToFile(textDirectory);
            ExtractItemsToFile(textDirectory);
            ExtractCharactersToFile(textDirectory);
            ExtractQuestsToFile(textDirectory);
            ExtractJournalToFile(textDirectory);
            ExtractBooksToFile(textDirectory);
            ExtractAbilitiesAndSpellsToFile(textDirectory);

            Debug.Log($"✓ All text data successfully extracted to CSV files! Processed {_processedStrings.Count} unique strings.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error during text extraction: {ex.Message}");
            Debug.LogError($"Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// 展开文本中的 `#IF/#THEN/#ELSE/#END` 条件分支。
    /// 流程：先清理技术性尾注，再匹配最外层条件块，
    /// 分别递归替换 then 与 else 分支，最终返回所有可见文本变体。
    /// </summary>
    public static List<string> ExpandConditionals(string input)
    {
        // 先移除诸如 `;;Scene:` 之类的技术注记
        input = Regex.Replace(input, @";;.*?$", "", RegexOptions.Multiline).Trim();

        var outputs = new List<string>();

        // 查找包含 THEN / ELSE 的条件块
        var match = Regex.Match(input, @"#IF\s*\(([^)]*)\)\s*#THEN\((.*?)\)(?:#ELSE\((.*?)\))?#END", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success)
        {
            outputs.Add(input.Trim());
            return outputs;
        }

        string fullMatch = match.Value;
        string thenPart = match.Groups[2].Value.Trim();
        string elsePart = match.Groups[3].Success ? match.Groups[3].Value.Trim() : "";

        // 仅在整段被一对外层引号包裹时去掉引号
        thenPart = StripQuotesIfWrapped(thenPart);
        elsePart = StripQuotesIfWrapped(elsePart);

        // 分别代入 then / else 分支，并继续递归展开剩余条件
        outputs.AddRange(ExpandConditionals(input.Replace(fullMatch, thenPart)));
        if (match.Groups[3].Success)
            outputs.AddRange(ExpandConditionals(input.Replace(fullMatch, elsePart)));

        return outputs;
    }

    // 如果首尾引号成对且内部没有嵌套引号，则去掉最外层引号
    private static string StripQuotesIfWrapped(string s)
    {
        if (s.StartsWith("\"") && s.EndsWith("\"") && s.Count(c => c == '"') == 2)
            return s.Substring(1, s.Length - 2).Trim();
        return s;
    }

    /// <summary>
    /// 从 IF-THEN-ELSE 结构中提取所有文本分支。
    /// 通过手动定位 THEN 和 ELSE 片段，生成可供后续提取的文本候选项。
    /// </summary>
    private static List<string> ExtractIfThenElseVariants(string input)
    {
        var result = new List<string>();
        
        // 手动查找 IF-THEN-ELSE 结构，避免嵌套括号或花括号导致正则误判
        int ifIndex = input.IndexOf("#IF(");
        while (ifIndex != -1)
        {
            // 查找与当前 IF 对应的 #END
            int endIndex = input.IndexOf("#END", ifIndex);
            if (endIndex == -1) break;
            
            string construct = input.Substring(ifIndex, endIndex - ifIndex + 4);
            
            // 提取 THEN 分支内容
            int thenIndex = construct.IndexOf("#THEN(");
            if (thenIndex != -1)
            {
                int thenStart = thenIndex + 6; // 跳过 "#THEN("
                int thenEnd = FindMatchingParenthesis(construct, thenStart - 1);
                if (thenEnd != -1)
                {
                    string thenText = construct.Substring(thenStart, thenEnd - thenStart).Trim();
                    if (!string.IsNullOrEmpty(thenText))
                    {
                        result.Add(Clean(thenText));
                    }
                }
            }
            
            // 如果存在 ELSE，则提取 ELSE 分支内容
            int elseIndex = construct.IndexOf("#ELSE(");
            if (elseIndex != -1)
            {
                int elseStart = elseIndex + 6; // 跳过 "#ELSE("
                int elseEnd = FindMatchingParenthesis(construct, elseStart - 1);
                if (elseEnd != -1)
                {
                    string elseText = construct.Substring(elseStart, elseEnd - elseStart).Trim();
                    if (!string.IsNullOrEmpty(elseText))
                    {
                        result.Add(Clean(elseText));
                    }
                }
            }
            
            // 继续查找下一个 IF 结构
            ifIndex = input.IndexOf("#IF(", endIndex);
        }
        
        return result;
    }
    
    /// <summary>
    /// 查找指定左括号对应的右括号位置。
    /// 通过计数方式处理嵌套括号，返回匹配右括号的索引。
    /// </summary>
    private static int FindMatchingParenthesis(string text, int openIndex)
    {
        if (openIndex >= text.Length || text[openIndex] != '(')
            return -1;
            
        int count = 1;
        for (int i = openIndex + 1; i < text.Length; i++)
        {
            if (text[i] == '(')
                count++;
            else if (text[i] == ')')
            {
                count--;
                if (count == 0)
                    return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// 提取引号文本和方括号动作文本，并拆分为独立片段。
    /// 用于把对白、动作描述和剩余文本分别整理出来。
    /// </summary>
    private static List<string> ExtractQuotesAndActions(string input)
    {
        var result = new List<string>();
        
        // 仅当文本包含引号或方括号时，才进入特殊拆分流程
        if (!input.Contains("\"") && !input.Contains("["))
            return result;
        
        string remaining = input;
        
        // 先提取方括号动作文本，如 [Action text]
        var bracketMatches = Regex.Matches(remaining, @"\[([^\]]+)\]");
        foreach (Match match in bracketMatches)
        {
            string actionText = match.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(actionText))
            {
                result.Add(Clean(actionText));
            }
            // 从剩余文本中移除已提取的方括号片段
            remaining = remaining.Replace(match.Value, " ");
        }
        
        // 提取带嵌套引号的文本，并区分外层文本和内层被引用内容
        // 处理类似："Call out to ""Joran the Usurper""" 这种结构
        var complexQuotePattern = @"""([^""]*?)""""([^""]+)""""([^""]*?)""";
        var complexMatches = Regex.Matches(remaining, complexQuotePattern);
        
        foreach (Match match in complexMatches)
        {
            // 分离为三段：内层引号前、内层引号内容、内层引号后
            string beforeQuote = match.Groups[1].Value.Trim();
            string innerQuote = match.Groups[2].Value.Trim();
            string afterQuote = match.Groups[3].Value.Trim();
            
            // 合并并加入外层文本（前半段 + 后半段）
            var outerParts = new List<string>();
            if (!string.IsNullOrEmpty(beforeQuote))
                outerParts.Add(beforeQuote);
            if (!string.IsNullOrEmpty(afterQuote))
                outerParts.Add(afterQuote);
            
            if (outerParts.Count > 0)
            {
                string outerText = string.Join(" ", outerParts).Trim();
                if (!string.IsNullOrEmpty(outerText))
                    result.Add(Clean(outerText));
            }
            
            // 将内层被引用文本作为独立片段加入结果
            if (!string.IsNullOrEmpty(innerQuote))
            {
                result.Add(Clean(innerQuote));
            }
            
            // 从剩余文本中移除已处理的引号片段
            remaining = remaining.Replace(match.Value, " ");
        }
        
        // 处理不含内层引号的普通引用文本
        var simpleQuotePattern = @"""([^""]+)""";
        var simpleMatches = Regex.Matches(remaining, simpleQuotePattern);
        
        foreach (Match match in simpleMatches)
        {
            string quotedText = match.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(quotedText))
            {
                result.Add(Clean(quotedText));
            }
            // 从剩余文本中移除已提取的普通引用文本
            remaining = remaining.Replace(match.Value, " ");
        }
        
        // 继续处理剩余未被引号或方括号覆盖的文本
        remaining = Regex.Replace(remaining, @"\s+", " ").Trim();
        if (!string.IsNullOrEmpty(remaining))
        {
            // 如果含有句末标点，则继续按句子拆分
            if (Regex.IsMatch(remaining, @"[\.!?]"))
            {
                string[] sentences = Regex.Split(remaining, @"(?<=[\.!?]['""]?)\s*(?=[""']?[A-ZА-Я])");
                foreach (var sentence in sentences)
                {
                    string cleaned = Clean(sentence);
                    if (!string.IsNullOrEmpty(cleaned))
                        result.Add(cleaned);
                }
            }
            else
            {
                string cleaned = Clean(remaining);
                if (!string.IsNullOrEmpty(cleaned))
                    result.Add(cleaned);
            }
        }
        
        return result;
    }

    /// <summary>
    /// 清理文本首尾的空白、引号和星号等噪声字符。
    /// 用于把提取出的短片段规整成可写入词条的形式。
    /// </summary>
    private static string Clean(string line)
    {
        return Regex.Replace(line, @"^[\s""'\*]+|[\s""'\*]+$", "").Trim();
    }

    /// <summary>
    /// 组装一行导出 CSV。
    /// 流程：分别转义原文和备注，再按 `Original;Translate;Comment` 结构输出。
    /// </summary>
    private static string CreateCSVLine(string original, string comment)
    {
        // 先转义 CSV 中可能冲突的字符
        original = EscapeCSV(original);
        comment = EscapeCSV(comment);
        
        return $"{original};;{comment}";
    }

    /// <summary>
    /// 对文本执行 CSV 转义。
    /// 流程：先把换行拍平成空格，再在必要时用双引号包裹并转义内部引号。
    /// </summary>
    private static string EscapeCSV(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        // 将换行替换为空格，避免打断 CSV 结构
        text = text.Replace("\n", " ").Replace("\r", " ");
        
        // 存在特殊字符时按 CSV 规范加引号
        if (text.Contains("\"") || text.Contains(";") || text.Contains("\n"))
        {
            text = text.Replace("\"", "\"\""); // 内部双引号需要转义成两个双引号
            text = $"\"{text}\"";
        }

        return text;
    }

    /// <summary>
    /// 将文本拆句后写入 CSV，并做全局去重。
    /// 流程：调用文本解析器拆分句子，过滤已写入内容，再逐条附带备注写入。
    /// </summary>
    private static void AddTextToCSV(StringBuilder csv, string text, string comment)
    {
        if (string.IsNullOrEmpty(text)) return;

        var sentences = GameTextParser.Parse(text);
        foreach (var sentence in sentences)
        {
            // 同一条文本仅写入一次
            if (!_processedStrings.Contains(sentence))
            {
                _processedStrings.Add(sentence);
                csv.AppendLine(CreateCSVLine(sentence, comment));
            }
        }
    }

    /// <summary>
    /// 导出场景标题、描述和选项文本。
    /// 流程：遍历项目栈中的场景容器和节点，把标题、描述与所有选项逐条写入 CSV。
    /// </summary>
    private static void ExtractSceneDataToFile(string directory)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Original;Translate;Comment");

        try
        {
            var projectStack = GetProjectStack();
            foreach (var project in projectStack)
            {
                if (project?.data?.sceneData?.list != null)
                {
                    foreach (var sceneContainer in project.data.sceneData.list)
                    {
                        if (sceneContainer?.list != null)
                        {
                            foreach (var nodeContainer in sceneContainer.list)
                            {
                                string sceneSource = nodeContainer.id;
                                
                                if (nodeContainer?.list != null)
                                {
                                    foreach (var sceneNode in nodeContainer.list)
                                    {
                                        string nodeId = sceneNode.id;
                                        
                                        // 场景标题
                                        AddTextToCSV(csv, sceneNode.title, $"Scene: {sceneSource}, Node: {nodeId}, Type: Title");
                                        
                                        // 场景描述
                                        AddTextToCSV(csv, sceneNode.description, $"Scene: {sceneSource}, Node: {nodeId}, Type: Description");
                                        
                                        // 场景选项
                                        if (sceneNode?.list != null)
                                        {
                                            for (int i = 0; i < sceneNode.list.Count; i++)
                                            {
                                                var exit = sceneNode.list[i];
                                                AddTextToCSV(csv, exit.option, $"Scene: {sceneSource}, Node: {nodeId}, Type: Option {i + 1}, Target: {exit.target ?? ""}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            csv.AppendLine(CreateCSVLine($"Error extracting scenes: {ex.Message}", "Error"));
        }

        File.WriteAllText(Path.Combine(directory, "scenes_dialogues.csv"), csv.ToString(), Encoding.UTF8);
        Debug.Log("Scenes and dialogues extracted to CSV");
    }

    /// <summary>
    /// 导出字符串列表中的文本内容。
    /// 遍历项目中的 `stringListData`，将每个条目的描述写入 CSV。
    /// </summary>
    private static void ExtractStringListsToFile(string directory)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Original;Translate;Comment");

        try
        {
            var projectStack = GetProjectStack();
            foreach (var project in projectStack)
            {
                if (project?.data?.stringListData?.list != null)
                {
                    foreach (var stringList in project.data.stringListData.list)
                    {
                        if (stringList?.list != null)
                        {
                            for (int i = 0; i < stringList.list.Count; i++)
                            {
                                var stringData = stringList.list[i];
                                AddTextToCSV(csv, stringData.description, $"StringList: {stringList.id}, Item: {i + 1}, ID: {stringData.id}");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            csv.AppendLine(CreateCSVLine($"Error extracting string lists: {ex.Message}", "Error"));
        }

        File.WriteAllText(Path.Combine(directory, "string_lists.csv"), csv.ToString(), Encoding.UTF8);
        Debug.Log("String lists extracted to CSV");
    }

    /// <summary>
    /// 导出所有物品相关文本。
    /// 会按不同物品分类分别提取名称、描述等可翻译内容。
    /// </summary>
    private static void ExtractItemsToFile(string directory)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Original;Translate;Comment");

        try
        {
            var projectStack = GetProjectStack();
            foreach (var project in projectStack)
            {
                var itemContainer = project.itemContainer;
                
                // 依次处理所有物品分类
                ExtractItemCategory(csv, "MELEE WEAPONS", itemContainer?.meleeWeapons?.list);
                ExtractItemCategory(csv, "RANGED WEAPONS", itemContainer?.rangedWeapons?.list);
                ExtractItemCategory(csv, "ARMOR", itemContainer?.armor?.list);
                ExtractItemCategory(csv, "SHIELDS", itemContainer?.shields?.list);
                ExtractItemCategory(csv, "CLOTHING", itemContainer?.clothing?.list);
                ExtractItemCategory(csv, "ACCESSORIES", itemContainer?.accessories?.list);
                ExtractItemCategory(csv, "FOOD", itemContainer?.foods?.list);
                ExtractItemCategory(csv, "CONSUMABLES", itemContainer?.consumeables?.list);
                ExtractItemCategory(csv, "REAGENTS", itemContainer?.reagents?.list);
                ExtractItemCategory(csv, "GEMS", itemContainer?.gems?.list);
                ExtractItemCategory(csv, "JEWELRY", itemContainer?.jewelry?.list);
                ExtractItemCategory(csv, "TRINKETS", itemContainer?.trinkets?.list);
                ExtractItemCategory(csv, "ADVENTURING ITEMS", itemContainer?.adventuringItems?.list);
                ExtractItemCategory(csv, "KEYS", itemContainer?.keys?.list);
                ExtractItemCategory(csv, "MISCELLANEOUS", itemContainer?.miscItems?.list);
            }
        }
        catch (Exception ex)
        {
            csv.AppendLine(CreateCSVLine($"Error extracting items: {ex.Message}", "Error"));
        }

        File.WriteAllText(Path.Combine(directory, "items.csv"), csv.ToString(), Encoding.UTF8);
        Debug.Log("Items extracted to CSV");
    }

    /// <summary>
    /// 导出指定物品分类中的文本。
    /// 对泛型列表中的每个物品读取常见文本字段并写入 CSV。
    /// </summary>
    private static void ExtractItemCategory<T>(StringBuilder csv, string categoryName, List<T> items) 
        where T : SKALDProjectData.ItemDataContainers.ItemData
    {
        if (items == null || items.Count == 0) return;
        
        try
        {
            foreach (var item in items)
            {
                AddTextToCSV(csv, item.title, $"Item: {item.id}, Category: {categoryName}, Field: Title");
                AddTextToCSV(csv, item.description, $"Item: {item.id}, Category: {categoryName}, Field: Description");
            }
        }
        catch (Exception ex)
        {
            csv.AppendLine(CreateCSVLine($"Error extracting {categoryName}: {ex.Message}", "Error"));
        }
    }

    /// <summary>
    /// 单独导出书籍内容。
    /// 除了基础信息外，还会保留书籍正文并按需要拆分长文本。
    /// </summary>
    private static void ExtractBooksToFile(string directory)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Original;Translate;Comment");

        try
        {
            var projectStack = GetProjectStack();
            foreach (var project in projectStack)
            {
                if (project?.itemContainer?.books?.list != null)
                {
                    foreach (var book in project.itemContainer.books.list)
                    {
                        AddTextToCSV(csv, book.title, $"Book: {book.id}, Field: Title");
                        AddTextToCSV(csv, book.description, $"Book: {book.id}, Field: Description");
                        
                        // 将书籍正文按规则拆分成多个片段
                        if (!string.IsNullOrEmpty(book.content))
                        {
                            var sentences = GameTextParser.Parse(book.content);
                            for (int i = 0; i < sentences.Count; i++)
                            {
                                csv.AppendLine(CreateCSVLine(sentences[i], $"Book: {book.id}, Field: Content, Part: {i + 1}"));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            csv.AppendLine(CreateCSVLine($"Error extracting books: {ex.Message}", "Error"));
        }

        File.WriteAllText(Path.Combine(directory, "books.csv"), csv.ToString(), Encoding.UTF8);
        Debug.Log("Books extracted to CSV");
    }

    /// <summary>
    /// 导出角色相关文本。
    /// 会遍历多个角色容器，提取角色名、描述等可翻译字段。
    /// </summary>
    private static void ExtractCharactersToFile(string directory)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Original;Translate;Comment");

        try
        {
            var projectStack = GetProjectStack();
            foreach (var project in projectStack)
            {
                var characterContainer = project.characterContainer;
                
                // 从所有角色容器中提取角色数据
                ExtractCharacterContainer(csv, "UNIQUE HUMANOIDS", characterContainer?.uniqueHumanoids?.list);
                ExtractCharacterContainer(csv, "COMMON HUMANOIDS", characterContainer?.commonHumanoids?.list);
                ExtractCharacterContainer(csv, "ANIMALS", characterContainer?.animals?.list);
                ExtractCharacterContainer(csv, "MONSTERS", characterContainer?.monsters?.list);
            }
        }
        catch (Exception ex)
        {
            csv.AppendLine(CreateCSVLine($"Error extracting characters: {ex.Message}", "Error"));
        }

        File.WriteAllText(Path.Combine(directory, "characters.csv"), csv.ToString(), Encoding.UTF8);
        Debug.Log("Characters extracted to CSV");
    }

    private static void ExtractCharacterContainer<T>(StringBuilder csv, string containerName, List<T> characters)
        where T : SKALDProjectData.CharacterContainers.Character
    {
        if (characters == null || characters.Count == 0) return;
        
        try
        {
            foreach (var character in characters)
            {
                AddTextToCSV(csv, character.title, $"Character: {character.id}, Type: {containerName}, Field: Title");
                AddTextToCSV(csv, character.description, $"Character: {character.id}, Type: {containerName}, Field: Description");
            }
        }
        catch (Exception ex)
        {
            csv.AppendLine(CreateCSVLine($"Error extracting {containerName}: {ex.Message}", "Error"));
        }
    }

    /// <summary>
    /// 导出任务相关文本。
    /// 包括不同任务容器中的标题、说明和其它描述内容。
    /// </summary>
    private static void ExtractQuestsToFile(string directory)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Original;Translate;Comment");

        try
        {
            var projectStack = GetProjectStack();
            foreach (var project in projectStack)
            {
                var questContainers = project.questContainers;
                
                if (questContainers?.mainQuests?.list != null)
                {
                    foreach (var quest in questContainers.mainQuests.list)
                    {
                        ExtractQuest(csv, quest, "MAIN QUEST");
                    }
                }
                
                if (questContainers?.sideQuests?.list != null)
                {
                    foreach (var quest in questContainers.sideQuests.list)
                    {
                        ExtractQuest(csv, quest, "SIDE QUEST");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            csv.AppendLine(CreateCSVLine($"Error extracting quests: {ex.Message}", "Error"));
        }

        File.WriteAllText(Path.Combine(directory, "quests.csv"), csv.ToString(), Encoding.UTF8);
        Debug.Log("Quests extracted to CSV");
    }

    /// <summary>
    /// 导出单个任务的文本内容。
    /// 根据任务对象中的字段写入对应的任务类型与说明信息。
    /// </summary>
    private static void ExtractQuest(StringBuilder csv, SKALDProjectData.QuestContainers.QuestData quest, string questType)
    {
        AddTextToCSV(csv, quest.title, $"Quest: {quest.id}, Type: {questType}, Field: Title");
        AddTextToCSV(csv, quest.begunDescription, $"Quest: {quest.id}, Type: {questType}, Field: Begun");
        AddTextToCSV(csv, quest.completedDescription, $"Quest: {quest.id}, Type: {questType}, Field: Completed");
        AddTextToCSV(csv, quest.failedDescription, $"Quest: {quest.id}, Type: {questType}, Field: Failed");
        AddTextToCSV(csv, quest.aboutDescription, $"Quest: {quest.id}, Type: {questType}, Field: About");
        AddTextToCSV(csv, quest.rewardDescription, $"Quest: {quest.id}, Type: {questType}, Field: Reward");
    }

    /// <summary>
    /// 导出日志文本。
    /// 遍历日志章节与条目，把可见文本写入 CSV。
    /// </summary>
    private static void ExtractJournalToFile(string directory)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Original;Translate;Comment");

        try
        {
            var projectStack = GetProjectStack();
            foreach (var project in projectStack)
            {
                var journalContainers = project.journalContainers;
                
                ExtractJournalContainer(csv, "Chapter 0", journalContainers?.chapter0Container?.list);
                ExtractJournalContainer(csv, "Chapter 1", journalContainers?.chapter1Container?.list);
                ExtractJournalContainer(csv, "Chapter 2", journalContainers?.chapter2Container?.list);
                ExtractJournalContainer(csv, "Characters", journalContainers?.charactersContainer?.list);
                ExtractJournalContainer(csv, "Miscellaneous", journalContainers?.miscContainer?.list);
            }
        }
        catch (Exception ex)
        {
            csv.AppendLine(CreateCSVLine($"Error extracting journal: {ex.Message}", "Error"));
        }

        File.WriteAllText(Path.Combine(directory, "journal.csv"), csv.ToString(), Encoding.UTF8);
        Debug.Log("Journal extracted to CSV");
    }

    private static void ExtractJournalContainer(StringBuilder csv, string chapterName, List<SKALDProjectData.JournalContainers.JournalEntry> entries)
    {
        if (entries == null || entries.Count == 0) return;
        
        try
        {
            foreach (var entry in entries)
            {
                AddTextToCSV(csv, entry.title, $"Journal: {chapterName}, Entry: {entry.id}, Field: Title");
                AddTextToCSV(csv, entry.description, $"Journal: {chapterName}, Entry: {entry.id}, Field: Description");
            }
        }
        catch (Exception ex)
        {
            csv.AppendLine(CreateCSVLine($"Error extracting {chapterName}: {ex.Message}", "Error"));
        }
    }

    /// <summary>
    /// 导出技能与法术文本。
    /// 会遍历多个能力与法术容器，提取名称和描述等字段。
    /// </summary>
    private static void ExtractAbilitiesAndSpellsToFile(string directory)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Original;Translate;Comment");

        try
        {
            var projectStack = GetProjectStack();
            foreach (var project in projectStack)
            {
                var abilities = project.abilityContainers;
                
                if (abilities?.spellContainer?.list != null)
                {
                    foreach (var spell in abilities.spellContainer.list)
                    {
                        AddTextToCSV(csv, spell.title, $"Ability: {spell.id}, Type: Spell, Field: Title");
                        AddTextToCSV(csv, spell.description, $"Ability: {spell.id}, Type: Spell, Field: Description");
                    }
                }
                
                if (abilities?.combatManeuverContainer?.list != null)
                {
                    foreach (var maneuver in abilities.combatManeuverContainer.list)
                    {
                        AddTextToCSV(csv, maneuver.title, $"Ability: {maneuver.id}, Type: Combat Maneuver, Field: Title");
                        AddTextToCSV(csv, maneuver.description, $"Ability: {maneuver.id}, Type: Combat Maneuver, Field: Description");
                    }
                }
                
                if (abilities?.additionAbilityContainer?.list != null)
                {
                    foreach (var ability in abilities.additionAbilityContainer.list)
                    {
                        AddTextToCSV(csv, ability.title, $"Ability: {ability.id}, Type: Passive Ability, Field: Title");
                        AddTextToCSV(csv, ability.description, $"Ability: {ability.id}, Type: Passive Ability, Field: Description");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            csv.AppendLine(CreateCSVLine($"Error extracting abilities: {ex.Message}", "Error"));
        }

        File.WriteAllText(Path.Combine(directory, "abilities_spells.csv"), csv.ToString(), Encoding.UTF8);
        Debug.Log("Abilities and spells extracted to CSV");
    }

    /// <summary>
    /// 获取插件目录路径。
    /// 用于确定文本导出文件和其它资源的目标位置。
    /// </summary>
    private static string GetPluginDirectory()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            return Path.GetDirectoryName(assembly.Location);
        }
        catch
        {
            return Application.dataPath;
        }
    }

    /// <summary>
    /// 从 `GameData` 中获取项目栈。
    /// 通过反射读取私有字段，拿到当前已加载的项目数据列表。
    /// </summary>
    private static List<SKALDProjectData> GetProjectStack()
    {
        try
        {
            // 使用反射访问私有字段 projectStack
            var gameDataType = typeof(GameData);
            var projectStackField = gameDataType.GetField("projectStack", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            return (List<SKALDProjectData>)projectStackField?.GetValue(null) ?? new List<SKALDProjectData>();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to get project stack: {ex.Message}");
            return new List<SKALDProjectData>();
        }
    }

    private static readonly char[] TrimChars = { ' ', '\t', '\r', '\n' };
    /// <summary>
    /// 在尽量不拆单词的前提下，将长文本拆分为长度不超过 `maxLen` 的多个片段。
    /// </summary>
    /// <param name="text">原始文本。</param>
    /// <param name="maxLen">每个片段允许的最大长度，默认 295。</param>
    /// <returns>拆分后的文本片段列表。</returns>
    public static List<string> SplitText(string text, int maxLen = 295)
    {
        if (maxLen < 1)
            throw new ArgumentException("maxLen должен быть положительным.", nameof(maxLen));

        var parts = new List<string>();
        if (string.IsNullOrEmpty(text))
            return parts;

        var current = new StringBuilder();

        foreach (var token in Regex.Split(text, @"(\s+)"))
        {
            if (token.Length == 0) continue;

            // 如果单个词本身就超过上限，则只能强制截断
            if (token.Length > maxLen)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString().TrimEnd(TrimChars));
                    current.Clear();
                }

                for (int i = 0; i < token.Length; i += maxLen)
                    parts.Add(token.Substring(i, Math.Min(maxLen, token.Length - i)));

                continue;
            }

            // 判断当前 token 是否还能放进缓冲区
            if (current.Length + token.Length > maxLen)
            {
                parts.Add(current.ToString().TrimEnd(TrimChars));
                current.Clear();
            }

            current.Append(token);
        }

        if (current.Length > 0)
            parts.Add(current.ToString().TrimEnd(TrimChars));

        return parts;
    }
}

public static class GameTextParser
{
    /* ─────────────────── 公共解析接口 ─────────────────── */

    public static List<string> Parse(string raw)
    {
        if (raw == null) throw new ArgumentNullException(nameof(raw));

        // 0) 去掉诸如 ";;Scene …" 的技术性尾注
        raw = PreNormalize(raw);
        raw = Regex.Replace(raw, @";;.*?$", "", RegexOptions.Multiline);

        // 1) 展开 #IF/#THEN/#ELSE/#END 条件结构
        var variants = ExpandFirstIf(raw);

        // 2) 将每个分支继续拆成可提取的句子
        var outList = new List<string>();
        foreach (var v in variants)
            outList.AddRange(SplitIntoSentences(v));

        return outList;
    }

    /* ──────────────── #IF / #THEN / #ELSE 处理 ──────────────── */

    private static List<string> ExpandFirstIf(string src)
    {
        // 带括号的标准形式
        const string PAT_PAREN =
            @"#IF\s*\([^\)]*\)\s*#THEN\s*\((.*?)\)(?:\s*#ELSE\s*\((.*?)\))?\s*#END";
        // 不带完整括号包裹的原始形式
        const string PAT_RAW =
            @"#IF\s*\([^\)]*\)\s*#THEN\s*(.+?)(?:#ELSE\s*(.+?))?\s*#END";

        var m = Regex.Match(src, PAT_PAREN,
                            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success)
            m = Regex.Match(src, PAT_RAW,
                            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success)
            return new List<string> { src.Trim() };

        string full     = m.Value;
        string thenPart = StripOuterQuotes(m.Groups[1].Value.Trim());
        string elsePart = m.Groups.Count > 2 && m.Groups[2].Success
                        ? StripOuterQuotes(m.Groups[2].Value.Trim())
                        : "";

        var list = new List<string>();
        string prefix = src.Substring(0, m.Index);
        string suffix = src.Substring(m.Index + full.Length);

        foreach (var v in ExpandFirstIf(prefix + thenPart + suffix))
            list.Add(v);
        if (elsePart.Length > 0)
            foreach (var v in ExpandFirstIf(prefix + elsePart + suffix))
                list.Add(v);

        return list;
    }

    /* ─────────────── 句子切分 ───────────── */

    private static IEnumerable<string> SplitIntoSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        
        // 将单个换行替换为 |NL|，避免换行文本被变为同行文本无法区分
        string flat = Regex.Replace(text, @"\r?\n+", "|NL|").Trim();
        flat = PreMaskAbbr(flat);

        // 按空行等方式切分段为多句
        string[] parts = Regex.Split(
            flat,
            @"(?<=[\.!?…]['""”)]?)\s+(?=[“""']?[A-Z])" +            // 标准句末后切分：句号/问号/感叹号/省略号后，跟着空白和一个新句的大写开头
            @"|(?<=[\.!?…]['""”])\s+(?=[a-z])" +                    // 句末引号后接小写时也切分，兼容 "Foo." bar 这类格式
            @"|(?<=[""”])\s+(?=[A-Z])" +                            // 结束引号后直接接大写开头的新句
            @"|(?<=:)\s+(?=[“""']?[A-Z])" +                         // 冒号后接标题式/说明式文本时切分，如 "Effect: Burning Hands"
            @"|(?<=,\s*[""“])\s*(?=[A-Z])" +                        // 逗号后接引号中的新短句，如 `, "Something"`
            @"|(?<=[\.!?…]['""”]),\s+(?=[A-Z])" +                   // 句末引号后还有逗号，再接新句时切分
            @"|(?<=,['""“”])\s+(?=[A-Za-z])" +                      // 引号内部逗号后的文本单独切开，兼容 quoted list / quoted clause
            @"|(?<=[\.!?…]['""”)]?)\s+[“""']?\.\.\.\s*(?=[A-Z])" +  // 句末后跟省略号，再接大写新句时切分
            @"|(?<=[\.!?…])\s+[""“”]\s*-\s+(?=[A-Z])" +             // 句末后接 `" - Title"` 这类引用或对话转折时切分
            @"|(?<=\|PAR\|)" +                                      // 遇到段落占位符 |PAR| 时强制切分
            @"|(?<=:)\s*(?=\|PAR\|)" +                              // 冒号后紧跟段落占位符时也切分，避免 `Title:|PAR|Body` 粘连
            @"|(?<=\|NL\|)" +                                       // 遇到单换行占位符 |NL| 时强制切分
            @"|(?<=:)\s*(?=\|NL\|)" +                               // 冒号后紧跟单换行占位符时切分                       
            @"|(?<=^\s*~)\s+(?=\S)" +                               // 句首波浪号后按空格切分，如 `~ Fire` -> `~` / `Fire`，保留波浪号本身
            @"|(?<=\|NL\|~)\s+(?=\S)" +                             // 单换行占位符后紧跟波浪号时，也在其后的空格切分，如 `|NL|~ Fire`
            @"|(?<=\S)\s*~\s*(?=\S)" +                              // 两侧都有非空白字符的波浪号分隔，如 `Fire ~ Ice`
            @"|(?<=:)\s+(?=[“""']?\{)" +                            // 冒号后直接进入占位符/变量块，如 `Effect: {PLAYER}`
            @"|(?<=[\.!?…])\s+(?=\(\s*[“""']?[A-Z])" +              // 句末后接括号补充说明，且括号内是大写开头的新段
            @"|(?<=\)[”""']?)\s+(?=[A-Z])" +                        // 右括号或右引号括号结束后，后面接大写新句
            @"|(?<=\)[”""']?)\s+\(\s*(?=[A-Z])" +                   // 一段以 `)` 结束后，后面紧跟新的括号段 `(Title...)`
            @"|(?<=[”""'])\)\s+\(\s*(?=[A-Z])" +                    // 引号包裹的括号段结束后，再接新的括号段
            @"|(?<=\.)\s+(?=[\+\-]\d)"                              // 句点后接数值变化项，如 `. +1`、`. -10%`
        );

        foreach (string raw in parts)
        {
            string s = raw.Trim();
            if (s.Length == 0) continue;

            s = CleanPart(s);

            var htmlParts = SplitHtmlParts(s);          // 剔除html标签并分割
            foreach (var html in htmlParts)
            {
                var curlyParts = SplitCurlyParts(html); // 处理占位替换符为所需内容并分割   
                foreach (var cur in curlyParts)
                {
                    var squareParts = SplitSquareBracketParts(cur); // 剔除中括号保留其内部文本并分割，例如 `[Spell]`
                    foreach (var part in squareParts)
                    {
                        string clean = CleanPart(part);
                        foreach (var sub in PostSplitCommaCaps(clean))
                        {
                            string final = CleanPart(sub);
                            if (final.Length > 0)
                                yield return final;
                        }
                    }
                }
            }
        }
    }

    /* ─────────────── 辅助函数 ─────────────── */

    private static readonly char[] QuoteChars = { '"', '“', '”', '«', '»' };
    private static bool IsQuote(char c) => Array.IndexOf(QuoteChars, c) >= 0;

    private static string StripOuterQuotes(string s)
    {
        s = s.Trim();
        if (s.Length < 2) return s;

        char first = s[0], last = s[s.Length - 1];
        bool pair = IsQuote(first) && IsQuote(last);

        if (pair &&
            s.IndexOf(first, 1) == -1 &&
            s.LastIndexOf(last, s.Length - 2) == -1)
            return s.Substring(1, s.Length - 2).Trim();

        return s;
    }

    private static string TrimEdges(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // ---- 删除开头多余的标记和符号 ----
        while (true)
        {
            if (s.StartsWith("|PAR|", StringComparison.Ordinal))
                s = s.Substring(5);
            else if (s.StartsWith("|NL|", StringComparison.Ordinal))
                s = s.Substring(4);
            else if (s.Length > 0 &&
                    (s[0] == Mask || char.IsWhiteSpace(s[0]) || s[0] == '*' ||
                    s[0] == '«' || s[0] == '»' || s[0] == '“' || s[0] == '”' ||
                    s[0] == '-' || s[0] == '.' || s[0] == '…' ||
                    s[0] == '('))
                s = s.Substring(1);
            else break;
        }

        // ---- 删除结尾多余的标记和符号 ----
        while (true)
        {
            if (s.EndsWith("|PAR|", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 5);
            else if (s.EndsWith("|NL|", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 4);
            else if (s.Length > 0 &&
                    (s[^1] == Mask || char.IsWhiteSpace(s[^1]) || s[^1] == '*' ||
                    s[^1] == '«' || s[^1] == '»' || s[^1] == '“' || s[^1] == '”' ||
                    s[^1] == '.' || s[^1] == '…' || s[^1] == ':' || 
                    s[^1] == '-' || s[^1] == ')')) 
                s = s.Substring(0, s.Length - 1);
            else break;
        }

        return s.Trim();
    }

    /// <summary>移除文本两端连续出现的引号。</summary>
    private static string StripOuterSentenceQuotes(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        /* 删除开头连续引号 */
        int start = 0;
        while (start < s.Length && IsQuote(s[start]))
            start++;
        s = s.Substring(start);

        if (s.Length == 0) return s;

        /* 删除结尾连续引号，并兼容 ". 这类结尾 */
        int end = s.Length - 1;
        while (end >= 0 &&
            (IsQuote(s[end]) ||
            (end > 0 && IsQuote(s[end - 1]) && ".!?".IndexOf(s[end]) >= 0)))
            end--;
        s = s.Substring(0, end + 1);

        return s.Trim();
    }

    /// <summary>
    /// 提取形如 `[Something]` 的动作文本，并同时从原字符串中移除。
    /// 返回列表中：
    /// 第 0 项为去掉方括号后的剩余文本（可能为空），
    /// 后续各项为去掉方括号后的动作文本。
    /// </summary>
    private static List<string> SplitSquareBracketParts(string src)
    {
        var list = new List<string>();
        var actions = Regex.Matches(src, @"\[[^\]]+\]");
        string without = Regex.Replace(src, @"\[[^\]]+\]", "").Trim();
        if (without.Length > 0) list.Add(without);

        foreach (Match m in actions)
        {
            string act = m.Value.Substring(1, m.Value.Length - 2).Trim();
            if (act.Length > 0) list.Add(act);
        }
        return list;
    }

    /// <summary>
    /// 提取 HTML 标签 `<tag>…</tag>` 中的文本内容。
    /// 返回列表中：
    /// 第 0 项为去掉标签后的剩余文本（可能为空），
    /// 后续各项为每个标签内部的纯文本内容。
    /// </summary>
    private static List<string> SplitHtmlParts(string src)
    {
        // 1) 按任意 HTML 标签 <...> 进行切分
        var tokens = Regex.Split(src, @"<[^>]+>");
        var list = new List<string>();

        foreach (var t in tokens)
        {
            string txt = t.Trim();
            if (txt.Length > 0)
                list.Add(txt);       // 仅保留非空文本片段
        }
        return list;
    }

    /// <summary>
    /// 处理 `{...}` 形式的占位符结构。
    /// • `{getName}` 会替换为 `{PLAYER}`
    /// • `{getMoney}` 会替换为 `{MONEY}`
    /// • `{addXp|300}` 会替换为 `300`
    /// • `{lordLady}` 会生成两条分支：`lord` / `lady`
    /// 其它 `{fooBar}` 形式的指令会被删除。
    /// </summary>
    private static List<string> SplitCurlyParts(string src)
    {
        // 初始只保留一个版本，即原始字符串
        var results = new List<string> { src };

        // 1. 先处理 lordLady 这种会产生分支的占位符
        for (int idx = 0; idx < results.Count; idx++)
        {
            string cur = results[idx];
            var m = Regex.Match(cur, @"\{lordLady\}", RegexOptions.IgnoreCase);
            if (!m.Success) continue;

            // 生成两个版本的字符串
            string lord = cur.Replace(m.Value, "lord");
            string lady = cur.Replace(m.Value, "lady");

            // 用第一个版本覆盖当前项，并插入第二个版本
            results[idx] = lord;
            results.Insert(idx + 1, lady);
        }

        // 2. 对每个版本继续做定向替换或删除
        for (int i = 0; i < results.Count; i++)
        {
            string line = results[i];

            // 一次遍历处理每个 {…} 指令：替换或删除
            line = Regex.Replace(
                line,
                @"\{([^{}]+)\}",                       // 捕获花括号内部的 token
                match =>
                {
                    string token = match.Groups[1].Value;

                    // ---- 1. getName → {PLAYER}
                    if (token.Equals("getName", StringComparison.OrdinalIgnoreCase))
                        return "{PLAYER}";

                    // ---- 2. addXp|NNN → NNN
                    if (token.StartsWith("addXp", StringComparison.OrdinalIgnoreCase))
                    {
                        int sep = token.IndexOf('|');
                        if (sep > 0 && int.TryParse(token.Substring(sep + 1), out int xp))
                            return xp.ToString();
                        return "0";
                    }

                    // ---- 3. getMoney / getGold → {MONEY}
                    if (token.StartsWith("getMoney", StringComparison.OrdinalIgnoreCase) ||
                        token.StartsWith("getGold",  StringComparison.OrdinalIgnoreCase))
                        return "{MONEY}";

                    // ---- 4. 其它 {fooBar} 指令统一删除
                    return string.Empty;
                },
                RegexOptions.IgnoreCase);

            // 最后再做一次收尾清理
            line = line.Trim();
            results[i] = line;
        }

        // 删除清理后变成空串的结果
        results.RemoveAll(string.IsNullOrEmpty);
        return results;
    }

    /// <summary>预处理文本：
    ///  – 删除只包含 * 的整行；
    ///  – 将连续两个换行替换为 |PAR| 标记，以保留空段落。</summary>
    private static string PreNormalize(string src)
    {
        // 0-a) 将 "# -IF" 规范化为 "#IF"
        src = Regex.Replace(src, @"#\s*-\s*IF", "#IF",
                            RegexOptions.IgnoreCase);

        // 0-b) 将 "-)#ELSE" 或 "-)#END" 规范化
        src = Regex.Replace(src, @"-\s*\)\s*#(ELSE|END)", ")#$1", RegexOptions.IgnoreCase);

        // 0-c) 将 ")#ELSE" 或 ")#END" 规范化
        src = Regex.Replace(src, @"\)\s*#(ELSE|END)", ")#$1", RegexOptions.IgnoreCase);

        // 1) 删除只包含 "*" 的整行
        src = Regex.Replace(src, @"^\s*\*\s*$", "", RegexOptions.Multiline);

        // 2) 如果某行以 ENTRY N 开头，则在其后插入 |PAR|
        src = Regex.Replace(src,
            @"^(ENTRY\s+\d+.*)$",
            "$1|PAR|",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // 3) 将双换行转换为 |PAR|
        src = Regex.Replace(src, @"\r?\n\s*\r?\n", "|PAR|");

        return src;
    }

    private const char Mask = '\uE000';   // 用于包裹缩写的掩码字符

    // 对 1 到 4 个字母加句点的缩写做掩码，避免在句子切分时被误判
    private static string PreMaskAbbr(string s)
    {
        foreach (var abbr in KnownAbbr)
        {
            // \b 表示单词边界，避免误匹配 'NPD.MG.' 这类文本
            string pattern = $@"\b{Regex.Escape(abbr)}";
            // 用 U+E000 将整个缩写 token 包起来
            s = Regex.Replace(
                    s, pattern,
                    m => $"{Mask}{m.Value}{Mask}",          // 保留原始大小写
                    RegexOptions.IgnoreCase);
        }
        return s;
    }

    private static string PostUnmaskAbbr(string s)
    {
        return s.Replace(Mask.ToString(), "");
    }

    private static IEnumerable<string> PostSplitCommaCaps(string line)
    {
        // 检查所有词语 token 是否都以大写字母开头
        // 这里允许连字符形式，例如 "Light Club"
        var words = Regex.Matches(line, @"\b[^\W\d_]+\b");
        if (words.Count == 0) { yield return line; yield break; }

        /*
        当出现如
        Bear's Strength, Serpent's Grace, Cure Moderate Poison, Aura of Fear, Instil Courage
        其中's、of这些会被拆分，导致识别为存在小写，故之后无法继续切分
        */
        var ignoredLowercaseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "s", "of", "the", "to"
        };
        bool allCapsStart = words.Cast<Match>()
                                .Where(m => !ignoredLowercaseWords.Contains(m.Value))
                                .All(m => char.IsUpper(m.Value[0]));
        if (!allCapsStart) { yield return line; yield break; }

        // 按逗号、数字分隔段或双空格继续切分
        foreach (var part in Regex.Split(line, @"\s*,\s*|\s+\d+\s+"))  
        {
            string p = part.Trim();
            if (p.Length > 0) yield return p;
        }
    }

    /// 对单个文本片段执行完整的最终清理。
    private static string CleanPart(string txt)
    {
        txt = StripOuterSentenceQuotes(txt);

        /* 去掉开头的 +1 / 1) / 1. 这类编号前缀 */
        txt = Regex.Replace(txt,
                    @"^[\+\-]?\d+(?:\.\d+)?%?\s*(?:[)\.]\s*|\s+)",
                    "");

        txt = PostUnmaskAbbr(txt);                  // 恢复缩写中的.符号
        
        if (Regex.IsMatch(txt.Trim(), @"^\{(PLAYER|MONEY)\}$", RegexOptions.IgnoreCase))
            return "";

        /* 去掉倍率标记，如 x1、x1.5、:x2 */
        txt = Regex.Replace(txt, @"\s*[:]?x\d+(\.\d+)?\b",
                            "", RegexOptions.IgnoreCase);

        /* 去掉不含字母的括号尾部内容，如 (10)、(02:00)、(5 */
        txt = Regex.Replace(txt,
                @"\s*\([^A-Za-z)]*\)\s*$", "");
        
        if (Regex.IsMatch(txt, @"^\s*\([^A-Za-z]*$"))
            return "";

        /* 去掉结尾的数字范围或分数，如 1-3、3/4、15 */
        txt = Regex.Replace(txt,
                @"\s+\d+(?:[-/]\d+)*\s*$", "");

        /* 如果最后只剩下纯数字 token，则直接丢弃 */
        if (Regex.IsMatch(txt, @"^\d+([./]\d+)*(\s*[A-Za-z]+)?$",
                        RegexOptions.IgnoreCase))
            return "";

        /* 合并重复空格 */
        txt = Regex.Replace(txt, @"\s{2,}", " ").Trim();

        /* 如果整行已经没有字母，则跳过 */
        if (!Regex.IsMatch(txt, @"[A-Za-z]"))
            return "";

        txt = TrimEdges(txt);
        return txt;
    }

    private static readonly string[] KnownAbbr =
    {
        "P.", "DMG.", "STR.", "DEX.", "INT.", "CHA.", "CON.",
        "HP.", "AC.", "DC.", "SPD.", "PER.", "WIS.", "AGI.",
        "LVL."
    };
}
