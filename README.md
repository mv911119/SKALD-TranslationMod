[SKALDAssembly_CN.zip](https://github.com/user-attachments/files/29035836/SKALDAssembly_CN.zip)
DLL位置是游戏文件下的C:\Program Files (x86)\Steam\steamapps\common\SKALD Against the Black Priory\SKALD Against the Black Priory_Data\Managed\SKALDAssembly.dll

这个是用dnSPY反编译的。反编译完直接搜索然后把里面英语的description改中文就行了。然后log里面大多没改，还给他修了点bug……我之前用“调谐”会崩溃因为它那个限制了字符长度，然后有一些小的直接用变量的我也改了改，我最恨那种变量名直接显示的，以至于现在有的标签都还是英文，但是有一些我写了转换功能放了进去转成纯文本显示。

============================================================================

[code and translation.zip](https://github.com/user-attachments/files/29049950/code.and.translation.zip)

这个是我如何进行的翻译工作的代码和生成的文件，具体来说，BepInEx是用AppData\LocalLow\High North Studios\SKALD_ Against the Black Priory\Custom Projects\DevelopmentBuild\Data\SkaldProject.json这个文件覆盖原来的unityasset里面的内容来达成汉化的。

在这个zip里面的py是我怎么把dat（用UABEA提取的Unity的数据，里面有个叫skaldproject的，转成dat单独导出。但是不知为何现在有4个，坑爹作者，得找对版本号）里的字抓出来转成json然后翻译好生成字体和language.json然后写回BepInEx在那个地址的skaldproject.json就可以用了。基本上我把json里的文字都抓出来翻译了。很大部分是AI，但是做了基本的校对，但是肯定还有错。这样有个好处是相当于所有的翻译都有了指针，起码一对一对应的是，哪里出问题了好找。

如果你想，用assetripper提取出来直接就是json甚至，都不用转来转去。
1和2都是导出相关的。
3是偷来的，来源是我fork之前的那个https://github.com/renshareck/SKALD-TranslationMod
4写回json的脚本非常重要，而且便于修改。

============================================================================

<img width="216" height="80" alt="Logo" src="https://github.com/user-attachments/assets/e474252c-da1e-407d-ac8a-f59f0d5a048b" />


此外做了个logo用于开始菜单。

我测试了一下基本可玩了！
感谢修复那个俄罗斯作者的破bug！我在上面纠结了好久

新的战斗的字体非常好用！

人工智能万岁……所以说实话我只翻译了术语什么的，统一了名词，但是没看剧情（不想被自己剧透）。剩下都是GPT完成的。

最后的最后，我没啥技术力，绝大部分都是AI的建议然后我去试。但是翻译的问题我能解决，毕竟咱也是翻译过克苏鲁大模组的人。
