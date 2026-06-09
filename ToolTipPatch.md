# SKALDTooltipPatch

> 帮我分析下src\Patches\TooltipTranslationPatch.cs代码及相关代码，和src\OriginSrc\ToolTipControl.cs代码，帮我分析下Patch代码具体Patch了哪些功能

<details><summary>9 previous messages</summary>

> Stream error
>
> Reconnecting 5/5
> request timed out
>
> 我先把 `TooltipTranslationPatch`、它依赖/调用的相关补丁代码，以及原始 `ToolTipControl` 的实现一起读一遍，先建立“原始行为 vs Patch 后行为”的对应关系，再给你拆解它具体拦截了哪些功能。
>
> <details><summary>Ran 3 commands</summary>
>
> - Ran `Get-ChildItem -Path src\Patches -Filter *Tooltip* -Recurse | Select-Object FullName`
> - Ran `Get-ChildItem -Path src\OriginSrc -Filter *ToolTipControl* -Recurse | Select-Object FullName`
> - Ran `rg -n "TooltipTranslationPatch|ToolTipControl|ShowFloatingTooltip|Set.*Tooltip|tooltip" src`
>
> </details>
>
> 我已经定位到关键点了：这个 Patch 不是去改 tooltip 的渲染样式，而是拦截“通过关键词查 tooltip 内容”的查表过程，把翻译后的关键词重新映射回原始英文 key。接下来我把原始 `ToolTipControl` 和补丁依赖的 `TranslationPatch` 细读一下，把整条调用链串起来。
>
> <details><summary>Ran 4 commands</summary>
>
> - Ran `Get-Content src\Patches\TooltipTranslationPatch.cs`
> - Ran `Get-Content src\OriginSrc\UITextBlock.cs | Select-Object -Index (620..700)`
> - Ran `Get-Content src\Patches\TranslationPatch.cs | Select-Object -Index (250..420)`
> - Ran `Get-Content src\OriginSrc\ToolTipControl.cs`
>
> </details>
>
> 现在链路已经比较清楚了：原版 `UITextBlock` 在点击高亮词时，会直接把 `highlightWord` 当 key 去 `ToolTipControl.ToolTipCategory.getToolTip()` 查字典；补丁的核心工作，就是保证“被翻译后的高亮词”也能查回原始 tooltip。我要再补几段关键实现，把这个映射是怎么建立的也一起说明清楚。
>
> <details><summary>Ran 5 commands</summary>
>
> - Ran ``$i=1; Get-Content src\Patches\TooltipTranslationPatch.cs | ForEach-Object {"$i`t$_"; $i++}``
> - Ran ``$i=1; Get-Content src\OriginSrc\ToolTipControl.cs | ForEach-Object {"$i`t$_"; $i++}``
> - Ran ``$i=1; Get-Content src\Patches\TranslationPatch.cs | ForEach-Object {"$i`t$_"; $i++} | Select-Object -Index (30..120)``
> - Ran ``$i=1; Get-Content src\OriginSrc\UITextBlock.cs | ForEach-Object {"$i`t$_"; $i++} | Select-Object -Index (655..675)``
> - Ran ``$i=1; Get-Content src\Patches\TranslationPatch.cs | ForEach-Object {"$i`t$_"; $i++} | Select-Object -Index (295..410)``
>
> </details>
>
> 我把关键证据都拿到了。下面会按“原版 tooltip 怎么工作”→“翻译补丁在哪一步介入”→“它实际 patch 到的功能边界”来拆，这样你看代码时会比较容易对上。
>
> <details><summary>Ran 5 commands</summary>
>
> - Ran `Get-Content src\Patches\TranslationPatch.cs | Select-Object -Index (1..80)`
> - Ran `rg -n "class UITextBlockSetContentPatch|TagKeys\(|TooltipKeyBuffer|identifyTooltipKeywords\(|HarmonyPatch.*UITextBlock|setContent" src\Patches\TranslationPatch.cs`
> - Ran `Get-Content src\Patches\TranslationPatch.cs | Select-Object -Index (410..470)`
> - Ran `Get-Content src\Patches\TranslationPatch.cs | Select-Object -Index (470..620)`
> - Ran `Get-Content src\Patches\TranslationPatch.cs | Select-Object -Index (620..760)`
>
> </details>
</details>

`TooltipTranslationPatch` 本身只 Patch 了一个点：`ToolTipControl.ToolTipCategory.getToolTip(string keyword)`，也就是“根据关键词查 tooltip 对象”的入口，[src/Patches/TooltipTranslationPatch.cs:33](./src/Patches/TooltipTranslationPatch.cs:33)、[src/OriginSrc/ToolTipControl.cs:184](./src/OriginSrc/ToolTipControl.cs:184)。

**原版逻辑**
原版 `ToolTipControl` 维护了两类 tooltip 集合：
- `Lore`：直接来自 `GameData.getTooltipsByCategories("Lore")`，[src/OriginSrc/ToolTipControl.cs:116](./src/OriginSrc/ToolTipControl.cs:116)
- `Rules`：除了 `GameData.getTooltipsByCategories("Rules")` 外，还额外把能力、法术、状态、属性、食物、消耗品、职业、背景、职业分支等对象也注册成 tooltip，[src/OriginSrc/ToolTipControl.cs:35](./src/OriginSrc/ToolTipControl.cs:35)

每个 tooltip 会把若干 `keyword` 注册进 `toolTips` 字典，[src/OriginSrc/ToolTipControl.cs:149](./src/OriginSrc/ToolTipControl.cs:149)。原版查询时非常直接：`getToolTip(keyword)` 只做字典命中，命中失败就报错返回 `null`，[src/OriginSrc/ToolTipControl.cs:184](./src/OriginSrc/ToolTipControl.cs:184)。而触发查询的地方在 `UITextBlock.getTooltipText()`，点击高亮词后直接拿 `word.highlightWord` 去查，[src/OriginSrc/UITextBlock.cs:656](./src/OriginSrc/UITextBlock.cs:656)。

**Patch 具体改了什么**
这个 Patch 不改 tooltip 内容来源，不改 tooltip UI，不改显示样式，只改“查 key 的时候如何兼容翻译后的文字”。

它的 Prefix 流程是：
1. 先判断当前语言是不是英文，英文则完全不接管，继续原版，[src/Patches/TooltipTranslationPatch.cs:74](./src/Patches/TooltipTranslationPatch.cs:74)
2. 非英文时，去 `UITextBlockSetContentPatch.TooltipKeyBuffer` 里查“翻译后的关键词 -> 原始关键词”的映射，[src/Patches/TooltipTranslationPatch.cs:80](./src/Patches/TooltipTranslationPatch.cs:80)
3. 如果没查到映射，放行原版 `getToolTip(keyword)`，[src/Patches/TooltipTranslationPatch.cs:89](./src/Patches/TooltipTranslationPatch.cs:89)
4. 如果查到了，比如“力量” -> `"Strength"`，它就手动反射调用原始 `getToolTip(originalKeyword)`，把结果塞进 `__result`，并 `return false` 跳过原方法，[src/Patches/TooltipTranslationPatch.cs:101](./src/Patches/TooltipTranslationPatch.cs:101)

所以它真正 Patch 的功能可以准确概括为：

- 支持点击“翻译后的高亮词”仍然弹出原 tooltip
- 把 tooltip 查询从“只能识别英文原 key”扩展为“优先把翻译 key 逆映射回英文 key”
- 英文环境不生效
- 映射失败时回退原版逻辑，不强行接管

**它依赖的相关代码**
`TooltipTranslationPatch` 单独看不完整，因为映射表不是它自己生成的，而是 `UITextBlockSetContentPatch` 在接管 `UITextBlock.setContent(string)` 时建立的，[src/Patches/TranslationPatch.cs:19](./src/Patches/TranslationPatch.cs:19)。

关键链路是：
1. `setContent` Patch 先调用原版的 `identifyTooltipKeywords(input)`，把原始文本里识别到的 tooltip 词包上 `<tag>...</tag>`，[src/Patches/TranslationPatch.cs:298](./src/Patches/TranslationPatch.cs:298)
2. `ExtractAndBufferTooltipKeys()` 从这些标签里提取原始 key，并翻译成目标语言 key，[src/Patches/TranslationPatch.cs:355](./src/Patches/TranslationPatch.cs:355)
3. `TagKeys()` 再去翻译后的文本里找到这些译文，把它们重新包回 `<tag>`，同时把“译文 -> 原文 key”写进 `TooltipKeyBuffer`，[src/Patches/TranslationPatch.cs:485](./src/Patches/TranslationPatch.cs:485)、[src/Patches/TranslationPatch.cs:598](./src/Patches/TranslationPatch.cs:598)
4. 这样等玩家点击中文高亮词时，`word.highlightWord` 就会是中文，而 `TooltipTranslationPatch` 能把它映射回英文 key，再走原始 tooltip 查询

**结论**
如果只问 `TooltipTranslationPatch.cs` “具体 Patch 了哪些功能”，答案就是一句话：

它 Patch 的是 `ToolTipControl.ToolTipCategory.getToolTip(string)` 的查询入口，使 `UITextBlock` 中点击到的“翻译后 tooltip 关键词”也能正确命中原版 tooltip 数据；它没有新增 tooltip 数据，也没有修改 tooltip 展示逻辑，只是做了查询前的 key 逆映射。

如果你愿意，我下一步可以继续帮你画一张更直观的“从 `UITextBlock.setContent` 到点击弹出 tooltip`”调用链图，把这几个 Patch 在整条链路里的职责拆开。

> 给我个调用图

可以，下面给你一个“tooltip 从文本生成到点击弹出”的调用图，重点标出 `TooltipTranslationPatch` 在哪里介入。

**调用图**

```text
UI 代码设置文本
  |
  |  UITextBlock.setContent(string input)
  |  src/OriginSrc/UITextBlock.cs
  |
  +--> 被 Patch：UITextBlockSetContentPatch.Prefix(...)
  |    src/Patches/TranslationPatch.cs
  |
  |    1. 翻译整段文本
  |    2. 调原版 identifyTooltipKeywords(input)
  |       找出原文里哪些词是 tooltip 关键词
  |    3. ExtractAndBufferTooltipKeys(...)
  |       提取 <tag>原始关键词</tag>
  |       生成映射：
  |       “翻译后关键词” -> “原始关键词”
  |    4. TagKeys(...)
  |       把翻译后文本里的对应词重新包上 <tag>
  |       同时写入 TooltipKeyBuffer
  |    5. ParseParagraphComplete(...)
  |       重新生成文字、颜色、高亮词等
  |
  v
UITextBlock 内部生成可点击的 tooltip word
  |
  |  每个高亮词最终带着：
  |  word.highlightWord
  |
  v
界面每帧绘制时
  |
  |  UITextBlock.draw(...)
  |  src/OriginSrc/UITextBlock.cs
  |
  +--> getTooltipText()
       src/OriginSrc/UITextBlock.cs:656
       |
       |  遍历 toolTipWords
       |  如果某个词被点击：
       |
       +--> this.toolTips.getToolTip(word.highlightWord)
            |
            |  this.toolTips 是 ToolTipControl.ToolTipCategory
            |  src/OriginSrc/ToolTipControl.cs
            |
            +--> 被 Patch：TooltipTranslationPatch.Prefix(...)
            |    src/Patches/TooltipTranslationPatch.cs
            |
            |    1. 若当前语言是 English：直接放行原版
            |    2. 若是中文等非英文：
            |       去 TooltipKeyBuffer 查
            |       “当前点击词” -> “原始英文 key”
            |    3. 查到：
            |       手动调用原始 getToolTip(originalKeyword)
            |       返回 tooltip
            |    4. 查不到：
            |       回退原版 getToolTip(keyword)
            |
            v
原始 ToolTipControl.ToolTipCategory.getToolTip(string keyword)
src/OriginSrc/ToolTipControl.cs:184
  |
  |  从 Dictionary<string, ToolTip> toolTips 查字典
  |
  +--> 命中后返回 ToolTip 对象
        |
        v
ToolTipPrinter.setToolTip(toolTip.getFullDescription(), this.toolTips)
src/OriginSrc/UITextBlock.cs:668
src/OriginSrc/ToolTipPrinter.cs
  |
  v
屏幕上弹出 tooltip
```

**你可以把它理解成两段职责**

1. `UITextBlockSetContentPatch`
负责“埋点”和“建映射表”。
也就是先在翻译阶段记住：
`中文词 -> 英文原始tooltip key`

2. `TooltipTranslationPatch`
负责“点击查询时反查”。
也就是点击中文词时，先还原回英文 key，再走游戏原版 tooltip 查表逻辑。

**最核心的一条链**

```text
点击高亮中文词
-> word.highlightWord
-> ToolTipCategory.getToolTip(中文词)
-> TooltipTranslationPatch
-> TooltipKeyBuffer[中文词] = 英文词
-> 原始 getToolTip(英文词)
-> 返回原版 ToolTip
-> ToolTipPrinter 显示出来
```

如果你要，我下一条可以继续给你画一个“类关系图 / 数据流图”，把 `UITextBlock`、`ToolTipControl`、`ToolTipPrinter`、`TooltipKeyBuffer` 四者的关系单独拎出来。

> 帮我逐行解释下src\Patches\TranslationPatch.cs中TagKeys的流程作用

<details><summary>4 previous messages</summary>

> 我先聚焦 `TagKeys` 这一段，把它按代码执行顺序拆开解释：它不只是“加标签”，还顺带解决了长词优先、重叠匹配和“翻译词 -> 原始 key”映射写回的问题。
>
> <details><summary>Ran 1 command</summary>
>
> - Ran ``$i=1; Get-Content src\Patches\TranslationPatch.cs | ForEach-Object {"$i`t$_"; $i++} | Select-Object -Index (480..625)``
>
> </details>
>
> 我顺手把它依赖的 `BuildPattern` 也看一下，这样我可以把“它到底按什么规则去匹配翻译后的词”一起讲清楚，不会只停留在表面。
>
> <details><summary>Ran 2 commands</summary>
>
> - Ran `rg -n "BuildPattern\(" src\Patches\TranslationPatch.cs`
> - Ran ``$i=1; Get-Content src\Patches\TranslationPatch.cs | ForEach-Object {"$i`t$_"; $i++} | Select-Object -Index (955..1005)``
>
> </details>
</details>

`TagKeys` 的核心作用是：在“已经翻译过的文本”里，把那些本来应该可点击的 tooltip 关键词重新找出来，包上 `<tag>...</tag>`，并顺手记录“翻译词 -> 原始英文 key”的映射，供后面的 `TooltipTranslationPatch` 反查使用，[src/Patches/TranslationPatch.cs:485](./src/Patches/TranslationPatch.cs:485)。

我按代码执行顺序给你逐段解释。

**函数入口**

[src/Patches/TranslationPatch.cs:485](./src/Patches/TranslationPatch.cs:485)

```csharp
public static string TagKeys(string text, Dictionary<string, string> dict)
```

这里两个参数的含义是：
- `text`：已经翻译后的整段文本
- `dict`：`翻译后关键词 -> 原始关键词` 的映射表

比如可能是：

```text
"力量" -> "Strength"
"发展点数" -> "Development Points"
```

它要做的是：在 `text` 中找到“力量”“发展点数”这些词，包成 `<tag>力量</tag>`，这样后续 `UITextBlock` 才会把它识别成可点击高亮词。

**1. 空值保护**

[src/Patches/TranslationPatch.cs:487](./src/Patches/TranslationPatch.cs:487)

```csharp
if (string.IsNullOrWhiteSpace(text) || dict == null)
    return text;
```

如果文本为空，或者映射表不存在，就没法做任何事情，直接返回原文本。
这一步只是防御式编程，避免后面空引用。

**2. 外层异常保护**

[src/Patches/TranslationPatch.cs:490](./src/Patches/TranslationPatch.cs:490)

```csharp
try
{
```

整个函数被一个大 `try/catch` 包住。
意思是：哪怕中间匹配逻辑出错，也尽量不要把整个文本渲染流程弄崩，最多记录日志，然后退回原文本。

**3. 准备“已处理区间”列表**

[src/Patches/TranslationPatch.cs:492](./src/Patches/TranslationPatch.cs:492)

```csharp
var processedRanges = new List<(int start, int end)>();
```

这个列表用来记录文本中哪些字符区间已经被选中准备打标签了。
它的目的很重要：防止多个关键词匹配到同一段文本，造成重叠包裹。

例如：
- 长词：`Development Points`
- 短词：`Points`

如果文本里有 `Development Points`，那短词 `Points` 也可能匹配到同一段的一部分。
`processedRanges` 就是用来做这种冲突裁决的。

---

**4. 为每个 key 构造正则模式，且长 key 优先**

[src/Patches/TranslationPatch.cs:495](./src/Patches/TranslationPatch.cs:495)

```csharp
var patternInfos = dict
    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
    .OrderByDescending(kvp => kvp.Key.Length)
```

这里先做两件事：
- 过滤掉空 key
- 按 key 长度从长到短排序

为什么要“长 key 优先”？
因为长词通常更具体，应该优先命中，避免被短词抢走。

比如：
- `Fire Resistance`
- `Fire`

显然应该先匹配 `Fire Resistance`。

**5. 对每个 key 调用 `BuildPattern` 构造模糊匹配正则**

[src/Patches/TranslationPatch.cs:499](./src/Patches/TranslationPatch.cs:499)、[src/Patches/TranslationPatch.cs:503](./src/Patches/TranslationPatch.cs:503)

```csharp
var pattern = BuildPattern(kvp.Key);
```

`BuildPattern` 的作用在这里：[src/Patches/TranslationPatch.cs:960](./src/Patches/TranslationPatch.cs:960)

它不是简单地精确匹配整词，而是构造一个“近似词干匹配”模式。
比如注释里举的例子是：

```csharp
// “Очки развития” -> “Очк\w* \s+ разви\w*”
```

具体做法是：
- 把 key 按空格拆成多个 token
- 每个 token 保留大约 65% 的前缀，但至少 3 个字符
- 后面接 `[\w\.-]*`，允许有词尾变化
- 最后用 `\s+` 把多个 token 连起来，并整体用 `\b...\b` 包住

也就是说它想解决的是：翻译后词语在文本里可能有轻微词形变化，仍然希望能匹配上。

**6. 为每个 key 生成 `PatternInfo`**

[src/Patches/TranslationPatch.cs:511](./src/Patches/TranslationPatch.cs:511)

```csharp
return (PatternInfo?)new PatternInfo
{
    OriginalKey = kvp.Value,
    Key = kvp.Key,
    Rx  = new Regex(
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
};
```

这里把每个映射项包装成一个结构体：
- `OriginalKey`：原始英文 key
- `Key`：翻译后的 key
- `Rx`：用于在翻译文本里查找这个 key 的正则

这一步之后，`patternInfos` 就变成了一组“可执行匹配规则”。

**7. 如果某个 key 构造正则失败，就跳过它**

[src/Patches/TranslationPatch.cs:520](./src/Patches/TranslationPatch.cs:520)

```csharp
catch (Exception ex)
{
    TranslationMod.Logger?.LogError(...)
    return (PatternInfo?)null;
}
```

单个关键词构造失败不会影响整个函数。
这说明作者很在意容错性。

---

**8. 开始收集所有匹配结果**

[src/Patches/TranslationPatch.cs:530](./src/Patches/TranslationPatch.cs:530)

```csharp
var allMatches = new List<(Match match, PatternInfo pattern, int priority)>();
```

这里不是一边找到一边替换，而是先把所有候选命中都收集起来。
这样做的好处是后面可以统一排序、去重、处理重叠。

**9. 用每个正则去扫描整段文本**

[src/Patches/TranslationPatch.cs:533](./src/Patches/TranslationPatch.cs:533)

```csharp
for (int i = 0; i < patternInfos.Length; i++)
{
    var p = patternInfos[i];
    ...
    var matches = p.Rx.Matches(text);
```

这里遍历每个 `PatternInfo`，拿它的正则去整段 `text` 中找匹配项。

其中 `i` 被用作优先级：
- 因为前面已经按 key 长度降序排过序
- 所以 `i` 越小，说明 key 越长，优先级越高

**10. 过滤掉已经带 `<tag>` 的内容**

[src/Patches/TranslationPatch.cs:541](./src/Patches/TranslationPatch.cs:541)

```csharp
if (!match.Value.Contains("<tag>") && !match.Value.Contains("</tag>"))
{
    allMatches.Add((match, p, i));
}
```

这一步是为了避免重复包裹。
如果某段文本本来就已经带标签了，就不要再套一层。

虽然这里判断的是 `match.Value` 是否含 `<tag>`，从纯严格性看不一定能完全覆盖所有情况，但它的意图很明确：避免二次包裹。

---

**11. 统一排序：先按优先级，再按位置**

[src/Patches/TranslationPatch.cs:554](./src/Patches/TranslationPatch.cs:554)

```csharp
allMatches = allMatches
    .OrderBy(x => x.priority)
    .ThenBy(x => x.match.Index)
    .ToList();
```

排序规则是：
- 先按 `priority`：长词优先
- 再按 `match.Index`：同优先级时，按文本出现顺序

这一步很关键，因为后面处理重叠时，谁先进入 `finalMatches`，谁就保住了自己的区间。

---

**12. 准备最终保留的匹配集合**

[src/Patches/TranslationPatch.cs:560](./src/Patches/TranslationPatch.cs:560)

```csharp
var finalMatches = new List<(Match match, PatternInfo pattern)>();
```

`allMatches` 是候选集。
`finalMatches` 才是真正准备替换的集合。

**13. 逐个检查候选命中是否与已选区间重叠**

[src/Patches/TranslationPatch.cs:563](./src/Patches/TranslationPatch.cs:563)

```csharp
foreach (var (match, pattern, _) in allMatches)
{
    int start = match.Index;
    int end = match.Index + match.Length - 1;
```

这里把每个匹配项转换成一个字符区间 `[start, end]`。

**14. 判断重叠**

[src/Patches/TranslationPatch.cs:568](./src/Patches/TranslationPatch.cs:568)

```csharp
bool overlaps = processedRanges.Any(range =>
    !(end < range.start || start > range.end));
```

这个条件等价于：“当前区间与某个已处理区间有交集”。

如果不重叠，就可以保留；如果重叠，就跳过。

**15. 不重叠则加入最终集合，并记录区间**

[src/Patches/TranslationPatch.cs:572](./src/Patches/TranslationPatch.cs:572)

```csharp
if (!overlaps)
{
    finalMatches.Add((match, pattern));
    processedRanges.Add((start, end));
}
```

这里体现了前面排序的意义：
- 长词先进入
- 后续与它冲突的短词会被丢掉

这正是作者想要的行为。

---

**16. 开始真正替换前，再按位置从后往前排**

[src/Patches/TranslationPatch.cs:588](./src/Patches/TranslationPatch.cs:588)

```csharp
finalMatches = finalMatches.OrderByDescending(x => x.match.Index).ToList();
```

这是字符串替换里很经典的技巧。

原因是：如果从前往后替换，前面插入 `<tag>` 会改变字符串长度，后面 `match.Index` 就失效了。
从后往前替换，就不会影响前面尚未处理的索引。

---

**17. 取出当前命中的原文本**

[src/Patches/TranslationPatch.cs:591](./src/Patches/TranslationPatch.cs:591)

```csharp
string matchValue = match.Value;
```

这里的 `matchValue` 就是翻译文本中实际命中的那一段，比如：
- `力量`
- `发展点数`
- `Fire Resistance` 的某种翻译形式

**18. 构造带标签的替换串**

[src/Patches/TranslationPatch.cs:596](./src/Patches/TranslationPatch.cs:596)

```csharp
string replacement = $"<tag>{matchValue}</tag>";
```

目的很直接：把命中的词标记成 tooltip 词。

---

**19. 将“翻译词 -> 原始 key”写入全局缓冲区**

[src/Patches/TranslationPatch.cs:598](./src/Patches/TranslationPatch.cs:598)

```csharp
if (!TooltipKeyBuffer.ContainsKey(matchValue) && matchValue != pattern.OriginalKey)
{
    TooltipKeyBuffer.Add(matchValue, pattern.OriginalKey);
}
```

这是这个函数最重要的副作用之一。

它把：
- 当前文本里实际显示/点击到的词 `matchValue`
- 对应回 tooltip 原始 key `pattern.OriginalKey`

存进 `TooltipKeyBuffer`。

例如：
- `"力量"` -> `"Strength"`
- `"潜行"` -> `"Stealth"`

后面用户点击这个高亮词时，`TooltipTranslationPatch` 就能拿它去反查原始英文 key。

这里额外判断了：

```csharp
matchValue != pattern.OriginalKey
```

意思是：如果翻译词和原始 key 一样，就没必要专门写映射。
这种情况通常出现在没被翻译、或原文和译文相同的时候。

---

**20. 用字符串切片完成替换**

[src/Patches/TranslationPatch.cs:604](./src/Patches/TranslationPatch.cs:604)

```csharp
text = text.Substring(0, match.Index) + replacement + text.Substring(match.Index + match.Length);
```

这里没有用 `Regex.Replace`，而是手动按索引拼接：
- 前半段原文
- `<tag>命中词</tag>`
- 后半段原文

因为此时它已经精确知道要替换哪些区间了，直接按索引改最稳。

**21. 单个替换失败也只记日志，不中断整个函数**

[src/Patches/TranslationPatch.cs:611](./src/Patches/TranslationPatch.cs:611)

```csharp
catch (Exception ex)
{
    TranslationMod.Logger?.LogError(...)
}
```

这又是一次局部容错。
说明作者希望“有些词没打上 tag 也可以接受，但不能把整段文本打崩”。

---

**22. 全部替换完成后返回新文本**

[src/Patches/TranslationPatch.cs:617](./src/Patches/TranslationPatch.cs:617)

```csharp
return text;
```

最终返回的是“翻译文本 + 重新标记后的 `<tag>`”。

---

**23. 外层兜底：整个函数出错就返回原文本**

[src/Patches/TranslationPatch.cs:619](./src/Patches/TranslationPatch.cs:619)

```csharp
catch (Exception ex)
{
    TranslationMod.Logger?.LogError(...)
    return text;
}
```

如果整个流程在全局层面出了问题，就不再做任何加工，直接返回当前文本。
这能保证 UI 至少还能显示，不至于因为 tooltip 标记失败而整个文本没了。

---

**把它总结成一句话**

`TagKeys` 做了 4 件事：

1. 根据“翻译后关键词列表”生成模糊匹配正则
2. 在翻译后的整段文本里找出这些关键词
3. 处理长词优先和重叠冲突后，给最终命中的词包上 `<tag>`
4. 同时把“界面上实际显示的翻译词”映射回“原始 tooltip key”，写进 `TooltipKeyBuffer`

**你看这段时最该注意的几个设计点**

- 它不是精确匹配，而是通过 `BuildPattern()` 做“词干近似匹配”，[src/Patches/TranslationPatch.cs:960](./src/Patches/TranslationPatch.cs:960)
- 它先全量收集匹配，再做去重和冲突裁决，不是一边找一边改
- 它强制“长词优先”
- 它从后往前替换，避免索引偏移
- 它不仅修改文本，还在维护后续点击 tooltip 所需的数据映射

如果你愿意，我下一条可以继续按同样方式，把 `BuildPattern()` 也逐行拆开讲一下，因为 `TagKeys` 的匹配效果很大程度上取决于它。

> 为什么要构造BuildPattern这种近似匹配，给我一个例子

因为 `TagKeys` 处理的不是“原始关键词表”，而是“整段已经被翻译过的自然语言文本”。翻译后，关键词在正文里不一定还保持和词条名完全一致，所以如果只做精确匹配，很多 tooltip 词会重新标记失败。

`BuildPattern()` 的目的，就是把一个词条名转换成“保留词干、允许词尾变化”的近似匹配规则。它在这里：[src/Patches/TranslationPatch.cs:960](./src/Patches/TranslationPatch.cs:960)

**为什么精确匹配不够**
举个最典型的例子，假设 tooltip key 翻译出来是：

```text
Development Point
```

但实际整句翻译后写进正文时，译文可能变成：

```text
You gained 3 Development Points.
```

这里正文里出现的是 `Development Points`，比词条名多了一个复数 `s`。
如果只按精确字符串 `Development Point` 去找，就匹配不到，结果这个词不会被包上 `<tag>`，后面点击也就出不来 tooltip。

而 `BuildPattern("Development Point")` 会把它变成近似规则，效果类似：

```regex
\bDevelopm[\w\.-]*\s+Poi[\w\.-]*\b
```

这样：
- `Development Point` 能匹配
- `Development Points` 也能匹配
- 某些轻微词形变化也能匹配

**再举一个多词短语的例子**
假设翻译 key 是：

```text
Fire Resistance
```

正文翻译后可能写成：

```text
Grants increased Fire Resistant bonuses
```

如果做精确匹配，`Fire Resistance` 和 `Fire Resistant` 不一样，就失配。
近似匹配会保留每个单词的前缀，比如：
- `Fire` -> `Fir[\w\.-]*`
- `Resistance` -> `Resist[\w\.-]*`

拼起来后，就有机会同时命中：
- `Fire Resistance`
- `Fire Resistant`

这就是它想解决的问题：**正文里的词形，往往不是 tooltip 词条名的机械复制。**

**代码里注释给的例子**
作者自己在注释里写了一个俄语例子：[src/Patches/TranslationPatch.cs:965](./src/Patches/TranslationPatch.cs:965)

```csharp
// “Очки развития” -> “Очк\w* \s+ разви\w*”
```

意思就是：
- 词条是“发展点数”
- 真实文本里可能出现带词尾变化的形式
- 所以只保留每个词前半截，后面放开

**一句话总结**
构造 `BuildPattern` 的原因是：

**tooltip 词条名是“标准名”，但翻译后的正文里经常出现它的复数、变格、变形、轻微改写版本；近似匹配是为了让这些正文形式仍然能被识别并重新打上 `<tag>`。**

如果你想，我可以下一条直接拿 `BuildPattern("Development Points")` 按代码一步一步手算出最终 regex 给你看。

> 对于中文是否不太需要这种近似匹配

对，**如果目标语言主要是中文，这种近似匹配的必要性会明显下降**，因为中文通常没有英文、俄语那种很常见的词形变化。

**为什么中文里需求更弱**
- 中文名词一般没有复数词尾变化，不会出现 `Point` / `Points` 这种问题
- 中文也没有明显的变格、性、数、时态词尾
- 很多 tooltip 词条在正文里往往就是原样出现，比如“力量”“潜行”“发展点数”

所以如果是纯中文语境，很多时候直接做：
- 精确匹配
- 或者最长词优先的精确匹配

就已经够用了。

**但不是完全不需要**
中文里虽然没有“词形变化”问题，但仍然可能有这几类不完全一致：

- 词条是“发展点数”，正文写成“获得了3点发展点数”
- 词条是“火焰抗性”，正文里可能写“火焰抗性值”
- 词条是“潜行”，正文里可能写“潜行能力”或“潜行检定”
- 翻译器有时会把同一个词在不同句子里翻成略有差异的表达

所以对中文来说，近似匹配不是为了处理“词尾变化”，更多是为了处理：
- 词条被嵌入更长短语
- 翻译措辞不完全一致
- 分词边界不明显

**结合这份实现来看**
当前 `BuildPattern()` 是按“保留前缀 + `[\w\.-]*`”设计的，[src/Patches/TranslationPatch.cs:960](./src/Patches/TranslationPatch.cs:960)。
这套思路更像是为英文/俄语这类语言准备的。

放到中文里会有两个现实问题：

- 收益没那么大，因为中文本身词形变化少
- 风险反而可能更高，因为中文没有天然单词边界，`\b` 和 `\w` 对中文的适配通常不如字母语言稳定，容易出现“该匹配的没匹配到”或“误匹配到更长词的一部分”

**我的判断**
如果你这个 Mod 主要服务中文，通常更合理的是：

1. 优先精确匹配
2. 再加“最长词优先”
3. 必要时针对中文单独做更保守的包含匹配策略
4. 不一定要沿用现在这种偏词干化的 `BuildPattern`

**一句话结论**
对中文来说，`BuildPattern` 这种近似匹配**不是完全没用，但确实没西语系/斯拉夫语系那么必要**；它在中文里的价值更多是兜底“措辞略变”，而不是处理真正的词形变化。

如果你愿意，我可以继续帮你分析一下：**当前这个 `BuildPattern` 在中文环境下具体会有哪些误匹配/漏匹配风险。**
