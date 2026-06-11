using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace TranslationMod.Patches
{
    /// <summary>
    /// 调整角色背包页签初始化时的多个纵向偏移：
    /// 1. `itemInteractionGrid` 在加入 `mainRow` 前上移 3 像素；
    /// 2. `textLable` 在加入 `mainRow` 前上移 10 像素。
    /// 3. `mainInventoryGrid` 在加入 `mainRow` 前上移 5 像素。
    /// 这里使用 Transpiler，是为了把修改精确插入到原始 `add(...)` 调用之前。
    /// </summary>
    [HarmonyPatch(typeof(UIInventorySheetCharacter), "initialize")]
    public static class UIInventorySheetCharacterPatch
    {
        // 预先缓存反射目标，避免在 Transpiler 循环里重复查找。
        private static readonly MethodInfo AddMethod = AccessTools.Method(typeof(UICanvas), nameof(UICanvas.add), new[] { typeof(UIElement) });
        private static readonly Type MainInventoryGridType = AccessTools.Inner(typeof(UIInventorySheetBase), "UIGridCharacterInventorySegment");
        private static readonly ConstructorInfo MainInventoryGridConstructor = MainInventoryGridType == null ? null : AccessTools.Constructor(MainInventoryGridType, new[] { typeof(int), typeof(int) });
        private static readonly FieldInfo MainRowField = AccessTools.Field(typeof(UIInventorySheetBase), "mainRow");
        private static readonly FieldInfo ItemInteractionGridField = AccessTools.Field(typeof(UIInventorySheetBase), "itemInteractionGrid");
        private static readonly FieldInfo MainInventoryGridField = AccessTools.Field(typeof(UIInventorySheetBase), "mainInventoryGrid");
        private static readonly MethodInfo AdjustItemInteractionGridMethod = AccessTools.Method(typeof(UIInventorySheetCharacterPatch), nameof(AdjustItemInteractionGrid));
        private static readonly MethodInfo AdjustTextLabelMethod = AccessTools.Method(typeof(UIInventorySheetCharacterPatch), nameof(AdjustTextLabel));
        private static readonly MethodInfo AdjustMainInventoryGridMethod = AccessTools.Method(typeof(UIInventorySheetCharacterPatch), nameof(AdjustMainInventoryGrid));

        [HarmonyTranspiler]
        /// <summary>
        /// 扫描 `initialize()` 的 IL，在目标 `mainRow.add(...)` 调用前分别插入补丁调用。
        /// 原方法相关片段大致可视为：
        /// `this.mainRow.add(this.itemInteractionGrid);`
        /// `this.mainRow.add(textLable);`
        /// `this.mainInventoryGrid = new UIInventorySheetBase.UIGridCharacterInventorySegment(11, 6);`
        /// `this.mainRow.add(this.mainInventoryGrid);`
        /// 我们通过匹配这两段栈准备指令来定位插入点，而不是依赖固定行号。
        /// </summary>
        private static IEnumerable<CodeInstruction> InitializeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            try
            {
                bool itemGridPatched = false;
                bool textLabelPatched = false;
                bool mainInventoryGridPatched = false;
                bool mainInventoryGridSizePatched = false;

                for (int i = 0; i < codes.Count; i++)
                {
                    if (!mainInventoryGridSizePatched && IsMainInventoryGridConstructorArgs(codes, i))
                    {
                        // 将 `new UIGridCharacterInventorySegment(11, 6)` 的第二个参数改为 5。
                        codes[i - 1] = new CodeInstruction(OpCodes.Ldc_I4_5);
                        mainInventoryGridSizePatched = true;
                        continue;
                    }

                    if (!CallsAdd(codes[i]))
                    {
                        continue;
                    }

                    if (!itemGridPatched && IsItemInteractionGridAdd(codes, i))
                    {
                        // 在 `this.mainRow.add(this.itemInteractionGrid)` 之前插入：
                        // `AdjustItemInteractionGrid(this);`
                        codes.InsertRange(i - 4, new[]
                        {
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(OpCodes.Call, AdjustItemInteractionGridMethod)
                        });
                        itemGridPatched = true;
                        i += 2;
                        continue;
                    }

                    if (!textLabelPatched && IsTextLabelAdd(codes, i))
                    {
                        // 在 `this.mainRow.add(textLable)` 之前插入：
                        // `AdjustTextLabel(textLable);`
                        // 这里复用原本压栈的局部变量加载指令，避免关心它具体是 ldloc.0 还是 ldloc.s。
                        codes.InsertRange(i - 3, new[]
                        {
                            new CodeInstruction(codes[i - 1]),
                            new CodeInstruction(OpCodes.Call, AdjustTextLabelMethod)
                        });
                        textLabelPatched = true;
                        i += 2;
                        continue;
                    }

                    if (!mainInventoryGridPatched && IsMainInventoryGridAdd(codes, i))
                    {
                        // 在 `this.mainRow.add(this.mainInventoryGrid)` 之前插入：
                        // `AdjustMainInventoryGrid(this);`
                        codes.InsertRange(i - 4, new[]
                        {
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(OpCodes.Call, AdjustMainInventoryGridMethod)
                        });
                        mainInventoryGridPatched = true;
                        i += 2;
                    }
                }

                if (!itemGridPatched || !textLabelPatched || !mainInventoryGridPatched || !mainInventoryGridSizePatched)
                {
                    TranslationMod.Logger?.LogError($"[UIInventorySheetCharacterPatch] Patch incomplete. itemGridPatched={itemGridPatched}, textLabelPatched={textLabelPatched}, mainInventoryGridPatched={mainInventoryGridPatched}, mainInventoryGridSizePatched={mainInventoryGridSizePatched}");
                }
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[UIInventorySheetCharacterPatch] Transpiler failed: {ex.Message}");
            }

            return codes;
        }

        /// <summary>
        /// `itemInteractionGrid` 是基类受保护字段，这里通过反射取出后统一按 `UIElement` 调整 padding。
        /// </summary>
        private static void AdjustItemInteractionGrid(UIInventorySheetCharacter instance)
        {
            var itemInteractionGrid = ItemInteractionGridField?.GetValue(instance) as UIElement;
            itemInteractionGrid?.setPaddingTop(+1);
        }

        /// <summary>
        /// `textLable` 的局部变量类型在这里无需精确声明为内部类，按 `UIElement` 处理即可。
        /// </summary>
        private static void AdjustTextLabel(UIElement textLable)
        {
            textLable?.setPaddingTop(-13);
        }

        /// <summary>
        /// `mainInventoryGrid` 同样是基类字段，这里通过反射取出并调整其顶部 padding。
        /// </summary>
        private static void AdjustMainInventoryGrid(UIInventorySheetCharacter instance)
        {
            var mainInventoryGrid = MainInventoryGridField?.GetValue(instance) as UIElement;
            mainInventoryGrid?.setPaddingTop(0);                // 取消设置
        }

        /// <summary>
        /// 判断当前指令是否为目标 `add(UIElement)` 调用。
        /// </summary>
        private static bool CallsAdd(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Callvirt && Equals(instruction.operand, AddMethod);
        }

        /// <summary>
        /// 匹配 `this.mainRow.add(this.itemInteractionGrid)` 的压栈模式：
        /// `ldarg.0 -> ldfld mainRow -> ldarg.0 -> ldfld itemInteractionGrid -> callvirt add`
        /// </summary>
        private static bool IsItemInteractionGridAdd(IReadOnlyList<CodeInstruction> codes, int addIndex)
        {
            return addIndex >= 4 &&
                   IsLdarg0(codes[addIndex - 4]) &&
                   LoadsField(codes[addIndex - 3], MainRowField) &&
                   IsLdarg0(codes[addIndex - 2]) &&
                   LoadsField(codes[addIndex - 1], ItemInteractionGridField);
        }

        /// <summary>
        /// 匹配 `this.mainRow.add(textLable)` 的压栈模式：
        /// `ldarg.0 -> ldfld mainRow -> ldloc.* -> callvirt add`
        /// 这里只要求最后一个参数来自局部变量，因为该位置正是目标 `textLable` 被加入时机。
        /// </summary>
        private static bool IsTextLabelAdd(IReadOnlyList<CodeInstruction> codes, int addIndex)
        {
            return addIndex >= 3 &&
                   IsLdarg0(codes[addIndex - 3]) &&
                   LoadsField(codes[addIndex - 2], MainRowField) &&
                   IsLdloc(codes[addIndex - 1]);
        }

        /// <summary>
        /// 匹配 `this.mainRow.add(this.mainInventoryGrid)` 的压栈模式：
        /// `ldarg.0 -> ldfld mainRow -> ldarg.0 -> ldfld mainInventoryGrid -> callvirt add`
        /// </summary>
        private static bool IsMainInventoryGridAdd(IReadOnlyList<CodeInstruction> codes, int addIndex)
        {
            return addIndex >= 4 &&
                   IsLdarg0(codes[addIndex - 4]) &&
                   LoadsField(codes[addIndex - 3], MainRowField) &&
                   IsLdarg0(codes[addIndex - 2]) &&
                   LoadsField(codes[addIndex - 1], MainInventoryGridField);
        }

        /// <summary>
        /// 匹配 `new UIGridCharacterInventorySegment(11, 6)` 的构造参数压栈模式：
        /// `ldc.i4.s 11 -> ldc.i4.6 -> newobj .ctor(int, int)`
        /// 这里只替换第二个高度参数，保持宽度参数不变。
        /// </summary>
        private static bool IsMainInventoryGridConstructorArgs(IReadOnlyList<CodeInstruction> codes, int instructionIndex)
        {
            return instructionIndex >= 2 &&
                   codes[instructionIndex].opcode == OpCodes.Newobj &&
                   Equals(codes[instructionIndex].operand, MainInventoryGridConstructor) &&
                   IsLdcI4Value(codes[instructionIndex - 2], 11) &&
                   IsLdcI4Value(codes[instructionIndex - 1], 6);
        }

        /// <summary>
        /// 判断一条指令是否为读取指定字段。
        /// </summary>
        private static bool LoadsField(CodeInstruction instruction, FieldInfo field)
        {
            return instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field);
        }

        /// <summary>
        /// 判断一条指令是否为加载当前实例 `this`。
        /// </summary>
        private static bool IsLdarg0(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldarg_0;
        }

        /// <summary>
        /// 兼容局部变量的不同 IL 编码形式。
        /// </summary>
        private static bool IsLdloc(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloc ||
                   instruction.opcode == OpCodes.Ldloc_0 ||
                   instruction.opcode == OpCodes.Ldloc_1 ||
                   instruction.opcode == OpCodes.Ldloc_2 ||
                   instruction.opcode == OpCodes.Ldloc_3 ||
                   instruction.opcode == OpCodes.Ldloc_S;
        }

        /// <summary>
        /// 统一判断不同 `ldc.i4*` 指令是否表示指定整数常量。
        /// </summary>
        private static bool IsLdcI4Value(CodeInstruction instruction, int value)
        {
            return instruction.opcode == OpCodes.Ldc_I4 && Equals(instruction.operand, value) ||
                   instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte signedByteValue && signedByteValue == value ||
                   instruction.opcode == OpCodes.Ldc_I4_M1 && value == -1 ||
                   instruction.opcode == OpCodes.Ldc_I4_0 && value == 0 ||
                   instruction.opcode == OpCodes.Ldc_I4_1 && value == 1 ||
                   instruction.opcode == OpCodes.Ldc_I4_2 && value == 2 ||
                   instruction.opcode == OpCodes.Ldc_I4_3 && value == 3 ||
                   instruction.opcode == OpCodes.Ldc_I4_4 && value == 4 ||
                   instruction.opcode == OpCodes.Ldc_I4_5 && value == 5 ||
                   instruction.opcode == OpCodes.Ldc_I4_6 && value == 6 ||
                   instruction.opcode == OpCodes.Ldc_I4_7 && value == 7 ||
                   instruction.opcode == OpCodes.Ldc_I4_8 && value == 8;
        }
    }
}
