# Filter condition groups

This document describes the staged implementation plan for hierarchical filter conditions.

## Current state

The filter editor currently stores conditions as a flat list. The formula is shown as generated text, so complex precedence still requires manual parentheses.

## Target model

A dataset filter should be represented as a tree:

```text
Group: or
  Group: and
    PART_TYPE = Арматура
    BOM_INCLUDE = 1
  Group: and
    PART_TYPE = Колонны
    level gt 0
```

The tree is then converted to a Model Studio style expression:

```text
([PART_TYPE] = "Арматура" and [BOM_INCLUDE] = "1") or ([PART_TYPE] = "Колонны" and [level] > "0")
```

## Added in this step

- `FilterConditionGroup` model.
- `FilterFormulaBuilder` service.
- Formula building from either a flat condition list or a nested condition group.

## Not added yet

- Visual nested group editor.
- Add group button.
- Move condition between groups.
- Remove condition row using a safe handler.
- Parsing arbitrary imported formulas into a full tree.

Those UI changes should be added after this foundation is merged and the project still builds in nanoCAD.