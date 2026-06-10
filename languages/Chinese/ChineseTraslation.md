# 中文翻译说明
## 说明
* 使用AI进行翻译，未进行任何人工矫正，望后来者可对其进行矫正优化
* 异色异底色等字体未进行修改，和正常字体一样
* 当前tiny及部分big字体大小做过修改，与原字体大小不同，详情查看font_en文件夹及build_font_assets.py文件配置

## 脚本说明
build_font_assets.py 一键运行，将translations中已翻译内生成中文字符图集fonts_cn并与fonts_en已有字符图集内容进行拼接至fonts目录

注意生成的fonts没有Logo.png，需要自行拷贝

# 编译
```shell
dotnet restore .\src\TranslationMod.csproj
dotnet build .\src\TranslationMod.csproj -c Release --no-restore
dotnet publish .\src\TranslationMod.csproj -c Release --no-build -o ..\src\build

```