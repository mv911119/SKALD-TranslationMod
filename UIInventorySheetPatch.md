# 梳理 UIInventorySheetBase 组件

> 帮我分析下
> src\OriginSrc\UIInventorySheetBase.cs
> 帮我梳理下其下有哪些组件

<details><summary>6 previous messages</summary>

> 我先读一下这个类和周边引用，重点看它声明/创建了哪些 UI 字段、容器和子组件，再按层级给你梳理。
>
> <details><summary>Ran 2 commands</summary>
>
> - Ran `Get-Content -Path src\OriginSrc\UIInventorySheetBase.cs`
> - Ran `rg "class UIInventorySheetBase|UIInventorySheetBase" -n src`
>
> </details>
>
> 这个文件本身是抽象基类，真正的页面布局由几个派生类在 `initialize()` 里拼出来。我顺手把这些派生类也看一下，这样组件关系不会只停留在“定义了哪些内部类”。
>
> <details><summary>Ran 5 commands</summary>
>
> - Ran `Get-Content -Path src\OriginSrc\UIInventorySheetCharacter.cs`
> - Ran `Get-Content -Path src\OriginSrc\UIInventorySheetContainer.cs`
> - Ran `Get-Content -Path src\OriginSrc\UIInventorySheetCampingFood.cs`
> - Ran `Get-Content -Path src\OriginSrc\UIInventorySheetMerchant.cs`
> - Ran `Get-Content -Path src\OriginSrc\UIInventorySheetCrafting.cs`
>
> </details>
>
> 我已经看到基类里的“标准组件库”和几个具体库存页的组合方式了。再取一下带行号的关键位置，方便你后续能直接跳到代码看。
>
> <details><summary>Ran 8 commands</summary>
>
> - Ran ``$i=1; Get-Content -Path src\OriginSrc\UIInventorySheetBase.cs | ForEach-Object {"$i`t$_"; $i++} | Select-Object -Index (0..180)``
> - Ran ``$i=1; Get-Content -Path src\OriginSrc\UIInventorySheetBase.cs | ForEach-Object {"$i`t$_"; $i++} | Select-Object -Index (240..430)``
> - Ran ``$i=1; Get-Content -Path src\OriginSrc\UIInventorySheetBase.cs | ForEach-Object {"$i`t$_"; $i++} | Select-Object -Index (460..690)``
> - Ran ``$i=1; Get-Content -Path src\OriginSrc\UIInventorySheetCharacter.cs | ForEach-Object {"$i`t$_"; $i++}``
> - Ran ``$i=1; Get-Content -Path src\OriginSrc\UIInventorySheetContainer.cs | ForEach-Object {"$i`t$_"; $i++}``
> - Ran ``$i=1; Get-Content -Path src\OriginSrc\UIInventorySheetCrafting.cs | ForEach-Object {"$i`t$_"; $i++} | Select-Object -Index (0..70)``
> - Ran ``$i=1; Get-Content -Path src\OriginSrc\UIInventorySheetCampingFood.cs | ForEach-Object {"$i`t$_"; $i++} | Select-Object -Index (0..80)``
> - Ran ``$i=1; Get-Content -Path src\OriginSrc\UIInventorySheetMerchant.cs | ForEach-Object {"$i`t$_"; $i++} | Select-Object -Index (0..80)``
>
> </details>
</details>

我梳理了一下，[UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:6) 是一个库存面板的抽象基类，本身不直接决定完整布局，而是提供一套通用库存 UI 组件，具体页面由 `UIInventorySheetCharacter / Container / Crafting / CampingFood / Merchant` 这些子类组装。

**基类主结构**

`UIInventorySheetBase : UICanvasHorizontal`

构造时会：

1. 开启横向拉伸：`stretchHorizontal = true`
2. 创建主纵向容器：`mainRow = new UICanvasVertical()`
3. 把 `mainRow` 加到自己下面
4. 调用抽象方法 `initialize()` 让子类补齐布局

核心字段在 [UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:247)：

- `mainRow`：主纵向容器
- `filterButtons`：物品分类过滤按钮
- `itemInteractionGrid`：角色模型 / 装备 / 工作台等交互区
- `mainInventoryGrid`：主库存格子，一般是队伍库存
- `secondaryInventoryGrid`：副库存格子，如容器、商人、工作台、今晚餐食
- `currentControllerSurface`：手柄当前聚焦区域
- `goldWeightBlock`：金币和负重文本

**基类内置组件**

1. `UIInventorySegment`
   位于 [UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:268)

   抽象库存段基类，继承 `UICanvasHorizontal`。主要负责记录交互结果：

   - `leftClickedInteractItem`
   - `doubleClickedInteractItem`
   - `rightClickedInteractItem`

   也就是说，后续主库存、副库存、装备区都通过它统一暴露“左键/右键/双击了哪个 Item”。

2. `UIGridCharacterInventorySegment`
   位于 [UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:307)

   这是标准库存格组件，内部包含：

   - `UIGridInventory grid`
   - `UIScrollbar scrollBar`
   - 当前 `Inventory`
   - 当前物品类型过滤 `itemTypes`
   - 网格宽度 `gridWidth`
   - 手柄选中列 `controllerSelectColumn`
   - 滚动偏移 `offsetIndex`

   构造时会创建 `UIGridInventory(width, height)`，再创建一个 `UIScrollbarStandard` 放在右侧。更新时会按 `getTypeFilter()` 过滤物品、根据滚动条裁剪列表，然后把结果喂给 `grid.update(...)`。

3. `TextLable`
   位于 [UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:447)

   注意类名拼写是 `TextLable`，不是 `TextLabel`。它是标题文本块，继承 `UITextBlock`，用于 `"Items Worn"`、`"Party Inventory"`、`"Container"` 等小标题。

4. `ItemInteractionUI`
   位于 [UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:466)

   抽象交互区，继承 `UIInventorySegment`。默认内部有一个：

   - `CharacterImage image`

   `CharacterImage` 继承 `UIImage`，用于显示角色/对象模型，底图固定加载：
   `Images/GUIIcons/InventoryUI/ModelFrame`

5. `ItemsWornUI`
   位于 [UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:523)

   角色装备栏交互区，继承 `ItemInteractionUI`。内部包含：

   - `CharacterImage image`，来自父类
   - `WornGridUI grid`
   - `UITextBlock infoBlock`

   `WornGridUI` 是一个 `UIGridLists(6, 2)`，一共 12 个装备槽：

   - 近战武器
   - 远程武器
   - 护甲
   - 盾牌
   - 弹药
   - 戒指
   - 头部
   - 衣服
   - 手套
   - 鞋
   - 光源/副手
   - 项链

6. `FilterButtons`
   位于 [UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:627)

   横向过滤按钮控件，继承 `UIButtonControlHorizontal`，构造参数是 `100 x 14`，按钮数 `8`。每个按钮是 `UITextButton`，但实际显示的是图标纹理。

   过滤类型有 8 组：

   - 全部：`FilterAll`
   - 武器：近战、远程、弹药
   - 护甲：护甲、衣服、盾
   - 饰品：头、手、脚、珠宝、戒指、项链
   - 消耗：消耗品、书卷
   - 食物
   - 冒险：杂项、试剂、冒险道具、光源、钥匙、书
   - 杂物：饰物、宝石

**具体页面组装**

`UIInventorySheetCharacter`
位于 [UIInventorySheetCharacter.cs](./src/OriginSrc/UIInventorySheetCharacter.cs:7)

结构大概是：

```text
mainRow
  TextLable("Items Worn")
  ItemsWornUI
    CharacterImage
    WornGridUI 6x2
    infoBlock
  TextLable("Party Inventory")
  mainInventoryGrid 11x6
    UIGridInventory
    UIScrollbar
  FilterButtons
  goldWeightBlock
```

`UIInventorySheetContainer`
位于 [UIInventorySheetContainer.cs](./src/OriginSrc/UIInventorySheetContainer.cs:13)

结构大概是：

```text
mainRow
  rows: UICanvasHorizontal
    firstRow: UICanvasVertical
      TextLable("Party Inventory")
      mainInventoryGrid 5x8
    secondRow: UICanvasVertical
      TextLable("Container")
      secondaryInventoryGrid 5x8
  FilterButtons
  goldWeightBlock
```

`UIInventorySheetMerchant`
位于 [UIInventorySheetMerchant.cs](./src/OriginSrc/UIInventorySheetMerchant.cs:8)

它继承 `UIInventorySheetContainer`，只是把第二列改成：

```text
secondRow
  TextLable("Merchant")
  secondaryInventoryGrid 5x7
  ServiceButtons
```

`UIInventorySheetCrafting`
位于 [UIInventorySheetCrafting.cs](./src/OriginSrc/UIInventorySheetCrafting.cs:8)

结构大概是：

```text
this: UICanvasHorizontal
  mainRow
    TextLable("Recipes")
    ListButtonControl
  secondRow
    TextLable("Workstation")
    IngredientsUI
      CharacterImage
      secondaryInventoryGrid 3x2
    UITechnicalButtonsHorizontal
      "Craft"
      "Clear"
    TextLable("Party Inventory")
    mainInventoryGrid 5x6
```

`UIInventorySheetCampingFood`
位于 [UIInventorySheetCampingFood.cs](./src/OriginSrc/UIInventorySheetCampingFood.cs:15)

结构大概是：

```text
this: UICanvasHorizontal
  mainRow
    TextLable("Activities")
    UITextSliderControl activities
  secondRow
    TextLable("Tonight's Meal")
    CookingUI
      secondaryInventoryGrid 5x2
    UITechnicalButtonsHorizontal
      "Clear"
    TextLable("Party's Food")
    mainInventoryGrid 5x6
```

**一句话总结**

这个文件下面的核心组件可以理解成：`主容器 mainRow` + `库存格 UIGridCharacterInventorySegment` + `装备/模型交互区 ItemInteractionUI` + `装备栏 ItemsWornUI` + `过滤按钮 FilterButtons` + `金币负重文本 goldWeightBlock`。不同库存页面只是把这些组件按不同布局组合起来：角色页偏“装备+队伍库存”，容器/商人页偏“双库存”，制作/露营页偏“左侧列表或活动 + 右侧工作区/餐食 + 队伍库存”。

> ok，接下来我主要关注UIInventorySheetCharacter、UIInventorySheetContainer，我想知道其中内部各组件是如何布局的，其所在x、y是如何确定的

<details><summary>10 previous messages</summary>

> 好，这个问题要顺着布局引擎看，单看 `initialize()` 只能看到添加顺序，看不到最终 `x/y` 怎么被算出来。我去追一下 `UICanvasHorizontal/Vertical`、`UIElement.add/alignElements` 和 padding/width/height 的实现。
>
> <details><summary>Ran 2 commands</summary>
>
> - Ran `rg "class UICanvas|class UICanvasHorizontal|class UICanvasVertical|alignElements|setPaddingLeft|padding" -n src\OriginSrc`
> - Ran `rg "class UIElement|setX\(|setY\(|getX\(|getY\(|setWidth\(|setHeight\(" -n src\OriginSrc`
>
> </details>
>
> 已经定位到布局规则了：这里坐标系有点“老派游戏 UI”味道，`x` 是左边缘，`y` 更像顶部基线/上边缘，纵向排布时后一个元素的 y 会往下减。接下来我把两种 Canvas 的精确算法和这两个页面代入。
>
> <details><summary>Ran 13 commands</summary>
>
> - Ran `Get-Content -Path src\OriginSrc\UICanvasVertical.cs`
> - Ran `Get-Content -Path src\OriginSrc\UICanvas.cs`
> - Ran `Get-Content -Path src\OriginSrc\UIElement.cs | Select-Object -Index (130..230)`
> - Ran `Get-Content -Path src\OriginSrc\UICanvasHorizontal.cs`
> - Ran `Get-Content -Path src\OriginSrc\UIElement.cs | Select-Object -Index (350..390)`
> - Ran `Get-Content -Path src\OriginSrc\UIElement.cs | Select-Object -Index (230..350)`
> - Ran `Get-Content -Path src\OriginSrc\UITextBlock.cs | Select-Object -Index (780..840)`
> - Ran `Get-Content -Path src\OriginSrc\UIScrollbarStandard.cs`
> - Ran `Get-Content -Path src\OriginSrc\UIGridBase.cs | Select-Object -Index (0..230)`
> - Ran `Get-Content -Path src\OriginSrc\UIGridInventory.cs`
> - Ran `Get-Content -Path src\OriginSrc\UIButtonControlBase.cs | Select-Object -Index (0..230)`
> - Ran `Get-Content -Path src\OriginSrc\UIGridBase.cs | Select-Object -Index (230..360)`
> - Ran `rg "InventoryGridButton|GridButton|ButtonInventory|InventoryUI" -n src\OriginSrc | Select-Object -First 80`
>
> </details>
>
> 关键规则已经足够清楚：`add()` 每次都会触发父容器重新 `alignElements()`，所以坐标不是在构造函数里一次性写死的，而是随着树挂载、尺寸变化、padding 变化不断被重新排版。现在我再补一下按钮/网格尺寸来源，尤其是库存格为什么看起来是 19 像素一档。
>
> <details><summary>Ran 3 commands</summary>
>
> - Ran `Get-Content -Path src\OriginSrc\UIElement.cs | Select-Object -Index (210..250)`
> - Ran `Get-ChildItem -Path . -Recurse -File -Include MenuBarBox* | Select-Object -First 20 FullName,Length`
> - Ran `Get-ChildItem -Path . -Recurse -File -Include ModelFrame* | Select-Object -First 20 FullName,Length`
>
> </details>
>
> 资产不在这个源码树里，所以具体某张图的像素宽高只能从代码中的设置和公式反推；不过 `UIGridCharacterInventorySegment` 明确用 `height * 19 - 1` 做滚动条高度，结合按钮宽高设置，库存格行高基本就是 19 像素节距。
>
> <details><summary>Ran 3 commands</summary>
>
> - Ran `rg "public virtual int getWidth" -n src\OriginSrc\UIElement.cs; Get-Content -Path src\OriginSrc\UIElement.cs | Select-Object -Index (190..225)`
> - Ran `rg "setTargetDimensions|SheetComplexInventory|SheetComplexContainer|grid.alignElements|gridUI" -n src\OriginSrc\GUIControl.cs src\OriginSrc\InventoryBaseState.cs src\OriginSrc\GUIControlInventory.cs src\OriginSrc\GUIControlContainer.cs`
> - Ran `Get-Content -Path src\OriginSrc\UIElement.cs | Select-Object -Index (0..35); Get-Content -Path src\OriginSrc\GUIControl.cs | Select-Object -Index (1900..2050)`
>
> </details>
>
> 这里还有外层 `GUIControl.SheetComplexInventory`：它把 `UIInventorySheetBase` 放进左列 `leftColumn`，所以最终屏幕坐标还要加上外层 Sheet/leftColumn 的坐标和 padding。你问的两个类内部布局，我会先用“相对该库存 sheet 左上角”的坐标讲，最后说明绝对坐标怎么叠加。
>
> <details><summary>Ran 5 commands</summary>
>
> - Ran `Get-Content -Path src\OriginSrc\UIElement.cs | Select-Object -Index (18..50); rg "protected class SheetComplexLeftHeavy|class SheetComplex" -n src\OriginSrc\GUIControl.cs`
> - Ran `Get-Content -Path src\OriginSrc\GUIControl.cs | Select-Object -Index (1250..1460)`
> - Ran `Get-Content -Path src\OriginSrc\GUIControl.cs | Select-Object -Index (1890..1930)`
> - Ran `Get-Content -Path src\OriginSrc\UIButtonControlHorizontal.cs; Get-Content -Path src\OriginSrc\UITextButtonControlHorizontalBase.cs | Select-Object -Index (0..90)`
> - Ran `Get-Content -Path src\OriginSrc\UIElement.cs | Select-Object -Index (540..580)`
>
> </details>
</details>

可以，把坐标规则先抓住就清楚了。

**核心布局规则**

这里所有子组件的 `x/y` 基本由父容器的 `alignElements()` 算出来，不是每个组件自己写死。

`UICanvasVertical` 纵向容器规则：

```csharp
child.x = parent.x + parent.padding.left
child.y = cursorY
cursorY -= child.getHeight()
```

其中：

```csharp
cursorY = parent.y - parent.padding.top
```

所以纵向排列时，越往后的元素 `y` 越小。

`UICanvasHorizontal` 横向容器规则：

```csharp
child.x = cursorX
child.y = parent.y + parent.padding.top
cursorX += child.getWidth()
```

其中：

```csharp
cursorX = parent.x + parent.padding.left
```

所以横向排列时，越往后的元素 `x` 越大。

**UIInventorySheetCharacter**

代码在 [UIInventorySheetCharacter.cs](./src/OriginSrc/UIInventorySheetCharacter.cs:7)。

结构是：

```text
UIInventorySheetCharacter
  mainRow: UICanvasVertical
    TextLable("Items Worn")
    ItemsWornUI
      CharacterImage
      WornGridUI 6x2
      infoBlock
    TextLable("Party Inventory")
    mainInventoryGrid 11x6
      UIGridInventory
      UIScrollbar
    FilterButtons
    goldWeightBlock
```

相对 `UIInventorySheetCharacter` 自己的左上角，`mainRow` 会被放在：

```text
mainRow.x = sheet.x
mainRow.y = sheet.y
```

因为基类 `UIInventorySheetBase` 是横向容器，只有一个 `mainRow`，没有额外 padding。

`mainRow` 是纵向排布，所以它下面的组件坐标类似：

```text
Items Worn label:
  x = mainRow.x
  y = mainRow.y

ItemsWornUI:
  x = mainRow.x
  y = label.y - label.height

Party Inventory label:
  x = mainRow.x + 5
  y = ItemsWornUI.y - ItemsWornUI.height

mainInventoryGrid:
  x = mainRow.x
  y = PartyLabel.y - PartyLabel.height

FilterButtons:
  x = mainRow.x
  y = mainInventoryGrid.y - mainInventoryGrid.height

goldWeightBlock:
  x = mainRow.x + 5
  y = FilterButtons.y - FilterButtons.height
```

这里 `Party Inventory` 标题设置了 `padding.left = 5`，所以它实际内容左移/右移由 `UITextBlock` 内部绘制时处理；但作为元素本身，它仍按纵向容器放在 `mainRow.x`。`goldWeightBlock.padding.left = 5` 同理，是文本内容偏移，不是元素自身 `x` 偏移。

`ItemsWornUI` 内部是横向排布：

```text
ItemsWornUI
  CharacterImage
  WornGridUI
  infoBlock
```

所以：

```text
CharacterImage.x = ItemsWornUI.x
WornGridUI.x     = CharacterImage.x + CharacterImage.width
infoBlock.x     = WornGridUI.x + WornGridUI.width
```

`mainInventoryGrid` 内部也是横向：

```text
UIGridCharacterInventorySegment
  UIGridInventory
  UIScrollbar
```

所以：

```text
UIGridInventory.x = segment.x
UIScrollbar.x     = segment.x + UIGridInventory.width
```

库存格再往下展开就是：

```text
UIGridInventory: UICanvasVertical
  Row 0
  Row 1
  Row 2
  ...

Row: UICanvasHorizontal
  Cell 0
  Cell 1
  Cell 2
  ...
```

因此第 `row/col` 个格子的坐标是：

```text
cell.x = grid.x + col * cellWidth
cell.y = grid.y - row * rowHeight
```

`rowHeight/cellWidth` 来自 `MenuBarBox` 纹理尺寸再减 1；代码里滚动条高度用了 `height * 19 - 1`，所以库存格基本按 19 像素节距设计。

**UIInventorySheetContainer**

代码在 [UIInventorySheetContainer.cs](./src/OriginSrc/UIInventorySheetContainer.cs:13)。

结构是：

```text
UIInventorySheetContainer
  mainRow: UICanvasVertical
    rows: UICanvasHorizontal, height = 164
      firstRow: UICanvasVertical, width = 106
        TextLable("Party Inventory")
        mainInventoryGrid 5x8
      secondRow: UICanvasVertical
        TextLable("Container")
        secondaryInventoryGrid 5x8
    FilterButtons
    goldWeightBlock
```

`mainRow` 依然在：

```text
mainRow.x = sheet.x
mainRow.y = sheet.y
```

`mainRow` 纵向排布：

```text
rows.x = mainRow.x
rows.y = mainRow.y

FilterButtons.x = mainRow.x
FilterButtons.y = rows.y - rows.height

goldWeightBlock.x = mainRow.x
goldWeightBlock.y = FilterButtons.y - FilterButtons.height
```

注意 `rows.setHeight(164)`，所以 `FilterButtons` 的 y 主要就是被这个 164 推下去的。

`rows` 是横向容器：

```text
firstRow.x = rows.x
firstRow.y = rows.y

secondRow.x = firstRow.x + firstRow.width
secondRow.y = rows.y
```

`firstRow.setWidth(106)`，所以第二列起点大致是：

```text
secondRow.x = rows.x + 106
```

`secondRow.padding.left = 8`，但这个 padding 影响的是 `secondRow` 内部元素的位置：

```text
Container label.x = secondRow.x + 8
secondaryGrid.x  = secondRow.x + 8
```

第一列内部：

```text
Party Inventory label.x = firstRow.x
Party Inventory label.y = firstRow.y

mainInventoryGrid.x = firstRow.x
mainInventoryGrid.y = label.y - label.height
```

第二列内部：

```text
Container label.x = secondRow.x + 8
Container label.y = secondRow.y

secondaryInventoryGrid.x = secondRow.x + 8
secondaryInventoryGrid.y = ContainerLabel.y - ContainerLabel.height
```

**绝对屏幕坐标怎么来**

这两个库存 sheet 不是直接画到屏幕上，而是被外层 `GUIControl.SheetComplexInventory` 加进 `leftColumn`：

```csharp
this.leftColumn.add(this.grid);
```

所以最终屏幕坐标是层层叠加：

```text
最终 x = SheetComplex.x
       + mainRow.padding.left
       + leftColumn.x/leftColumn.padding.left
       + UIInventorySheetBase 内部相对 x

最终 y = SheetComplex.y
       - tab/header/mainRow 前面元素高度
       - leftColumn/mainRow 内部纵向累计高度
       + UIInventorySheetBase 内部相对 y
```

简单说：
`UIInventorySheetCharacter / Container` 内部只决定“相对库存面板左上角”的布局；真正屏幕上的 `x/y` 还要加上外层 `SheetComplex -> mainRow -> leftColumn -> grid` 的排版结果。

> 帮我检查下，CharacterImage、WornGridUI、infoBlock中由哪些组成

<details><summary>2 previous messages</summary>

> 我再往下追这三个组件的父类实现：`CharacterImage` 主要看 `UIImage`，`WornGridUI` 要看 `UIGridLists/UIGridBase/UIGridRow`，`infoBlock` 要看 `UITextBlock` 的文本拆分结构。
>
> <details><summary>Ran 4 commands</summary>
>
> - Ran `Get-Content -Path src\OriginSrc\UIImage.cs`
> - Ran `Get-Content -Path src\OriginSrc\UIGridLists.cs`
> - Ran `Get-Content -Path src\OriginSrc\UITextBlock.cs | Select-Object -Index (0..260)`
> - Ran `Get-Content -Path src\OriginSrc\UITextBlock.cs | Select-Object -Index (260..760)`
>
> </details>
</details>

检查下来，这三个都是 `ItemsWornUI` 里的子组件，创建位置在 [UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:526)。

**CharacterImage**

定义在 [UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:494)，继承 `UIImage`。

它本身没有再包含子 UIElement，主要由这些数据组成：

- `backgroundTexture`：固定加载 `Images/GUIIcons/InventoryUI/ModelFrame`
- 角色/对象模型贴图：通过 `setImage(texture)` 叠加到 `backgroundTexture` 上
- `stretchVertical = true`
- `stretchHorizontal = true`

也就是说它不是一个容器，而是一个单图片元素：底框 `ModelFrame` + 人物模型 overlay。

**WornGridUI**

定义在 [UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:600)，继承链是：

```text
WornGridUI
  -> UIGridLists
    -> UIGridBase
      -> UICanvasVertical
```

构造是：

```csharp
public WornGridUI() : base(6, 2)
```

所以内部结构是：

```text
WornGridUI: 纵向网格
  UIGridRow 0: 横向按钮行
    UIGridButton 0 近战武器
    UIGridButton 1 远程武器
    UIGridButton 2 护甲
    UIGridButton 3 盾牌
    UIGridButton 4 弹药
    UIGridButton 5 戒指

  UIGridRow 1: 横向按钮行
    UIGridButton 0 头部
    UIGridButton 1 衣服
    UIGridButton 2 手套
    UIGridButton 3 鞋
    UIGridButton 4 光源/副手
    UIGridButton 5 项链
```

每个 `UIGridButton` 是 `UIButtonControlBase.UITextButton` 的子类，背景图来自：

```text
Images/GUIIcons/InventoryUI/MenuBarBox
Images/GUIIcons/InventoryUI/MenuBarBoxHover
Images/GUIIcons/InventoryUI/MenuBarBoxRightClick
```

空槽位会显示对应 backup icon，比如：

```text
WornIconMelee
WornIconRanged
WornIconArmor
WornIconShield
...
```

有装备时则显示 `item.getGridIcon()`。

**infoBlock**

创建在 [UIInventorySheetBase.cs](./src/OriginSrc/UIInventorySheetBase.cs:530)：

```csharp
this.infoBlock = new UITextBlock(100, 0);
this.infoBlock.padding.left = 4;
this.infoBlock.padding.top = 2;
this.infoBlock.setTabWidth(40);
```

它继承 `UITextBlock -> UICanvasVertical`，所以本质是一个文本容器。设置内容后会动态拆成：

```text
infoBlock: UITextBlock / UICanvasVertical
  Line: UICanvasHorizontal
    Word: UITextBlock.Word / UICanvasHorizontal
      Letter
      Letter
      Letter
  Line
    Word
      Letter...
```

所以 `infoBlock` 不是单个纹理文本，而是：多行 `Line`，每行多个 `Word`，每个词由多个 `Letter` 组成。它还支持：

- tab 对齐，当前 tab 宽度是 `40`
- 字符颜色
- 阴影色
- tooltip 词高亮
- `<color>` / `<tag>` / header 标签解析
- 自动换行和高度裁剪

在 `ItemsWornUI.setInfoBlock(content)` 里，会设置内容和阴影色：

```csharp
infoBlock.setContent(content);
infoBlock.setLetterShadowColor(C64Color.SmallTextShadowColor);
```

整体关系可以简化成：

```text
ItemsWornUI: UICanvasHorizontal
  CharacterImage: UIImage
  WornGridUI: UIGridLists 6x2
    Row 0
      6 个 UIGridButton
    Row 1
      6 个 UIGridButton
  infoBlock: UITextBlock
    多个 Line
      多个 Word
        多个 Letter
```

> 我能否对alignElements进行Patch，使得当上一个对象为TextLable时，下一个分配对象y自动加上5；上一个对象为ItemsWornUI，下一个分配对象y自动加上20

<details><summary>1 previous message</summary>

> 可以做，但我先不直接改：这里动的是布局核心函数，影响面很大。我把可行性和更稳的做法讲清楚，避免为了角色库存页一个间距，把全游戏所有纵向 Canvas 都牵连进去。
</details>

可以 Patch，但不建议直接改全局 `UICanvasVertical.alignElements()`。

原因是 [UICanvasVertical.cs](./src/OriginSrc/UICanvasVertical.cs:59) 是所有纵向布局容器共用的。你在这里写：

```csharp
if (previous is TextLable) ...
if (previous is ItemsWornUI) ...
```

会影响全项目所有 `UICanvasVertical`，包括弹窗、角色面板、列表、提示框等。而且 `TextLable`、`ItemsWornUI` 是 `UIInventorySheetBase` 的 `protected` 内部类，`UICanvasVertical` 这种外部基类不能干净地直接 `is UIInventorySheetBase.TextLable`，除非用类型名字符串反射式判断，比较脆。

更推荐两种做法。

**方案 1：用 padding，最简单**

当前纵向布局规则是：

```csharp
child.y = cursorY;
cursorY -= child.getHeight();
```

而 `getHeight()` 会包含元素自身 padding。所以你想“某元素后面多留空”，本质上可以改前一个元素的 `padding.bottom`。

例如在 [UIInventorySheetCharacter.cs](./src/OriginSrc/UIInventorySheetCharacter.cs:10)：

```csharp
UIInventorySheetBase.TextLable itemsWornLabel = new UIInventorySheetBase.TextLable("Items Worn");
itemsWornLabel.padding.bottom += 5;
this.mainRow.add(itemsWornLabel);

this.itemInteractionGrid = new UIInventorySheetBase.ItemsWornUI();
this.itemInteractionGrid.padding.bottom += 20;
this.mainRow.add(this.itemInteractionGrid);
```

这样不会动布局引擎，只影响 `UIInventorySheetCharacter`。

注意：坐标系里往下排是 `y` 变小，所以“下一个对象往下空 5 像素”，实际是让下一个对象的 `y` 数值更小，而不是更大。

**方案 2：给库存页专用 mainRow**

如果你确实想“规则化”：上一个是 `TextLable` 就多 5，上一个是 `ItemsWornUI` 就多 20，可以在 `UIInventorySheetBase` 内部做一个专用 `UICanvasVertical`，不要改全局 `UICanvasVertical`。

大概是：

```csharp
protected class InventoryMainRow : UICanvasVertical
{
    public override void alignElements()
    {
        int y = this.getY() - this.padding.top;
        UIElement previous = null;

        foreach (UIElement element in base.getElements())
        {
            if (!element.fixedPosition)
            {
                if (previous is UIInventorySheetBase.TextLable)
                {
                    y -= 5;
                }
                else if (previous is UIInventorySheetBase.ItemsWornUI)
                {
                    y -= 20;
                }

                element.setX(this.getX() + this.padding.left);
                element.setY(y);
                y -= element.getHeight();
            }

            element.alignElements();
            previous = element;
        }
    }
}
```

然后把基类构造里的：

```csharp
this.mainRow = new UICanvasVertical();
```

换成：

```csharp
this.mainRow = new UIInventorySheetBase.InventoryMainRow();
```

这个方案能访问 `TextLable / ItemsWornUI`，因为它定义在 `UIInventorySheetBase` 内部，且影响范围只限库存 Sheet 的 `mainRow`。

我个人建议先用 **方案 1 padding**。它更符合这个 UI 框架现有思路，改动小，风险低；如果后面多个库存页都需要同一套“类型后置间距规则”，再抽成 `InventoryMainRow`。

> 等等，padding能变为负数吗？因为我需要的是两个格子间间隔变小，甚至下一个格子要覆盖上一个格子部分

可以。这个项目里 `padding` 已经有负数用法，比如：

- `rightScrollBar.padding.top = -1`
- `modelPortrait.padding.bottom = -5`
- `leftScrollBar.padding.top = -1`

所以从代码实现看，`padding` 没有限制必须为正。

对 `UICanvasVertical` 来说，关键是：

```csharp
child.y = cursorY;
cursorY -= child.getHeight();
```

而普通 `UIElement.getHeight()` 会算：

```csharp
height = max(textureHeight + padding.top + padding.bottom, dimensions.height)
```

所以如果你想让“下一个元素更靠近上一个元素”，可以给上一个元素设置负的 `padding.bottom`：

```csharp
this.itemInteractionGrid.padding.bottom = -10;
```

这样上一个元素的 `getHeight()` 变小，下一个元素的 `y` 就不会被往下推那么多。

如果要覆盖更多，可以继续负：

```csharp
this.itemInteractionGrid.padding.bottom = -20;
```

不过有两个注意点：

1. 如果元素设置了固定 `dimensions.height`，`getHeight()` 会取 `max(calculatedHeight, dimensions.height)`，负 padding 不一定能继续压缩。
2. 负 padding 只影响布局占位，不会裁剪绘制，所以重叠是可行的，但鼠标 hover/点击区域可能仍按各自元素的 `x/y/width/height` 判断，重叠区的交互优先级要实际测一下。

你的场景里，优先试这个：

```csharp
this.itemInteractionGrid.padding.bottom = -20;
```

如果是想让 `Party Inventory` 标题或库存格更贴近 `ItemsWornUI`，这个方向是对的。



需要Patch UIInventorySheetCharacter、UIInventorySheetContainer ，添加负数Padding尝试