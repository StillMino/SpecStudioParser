# Filter editor model

The canonical editable filter structure is `DatasetConfig.RootFilterItems`.

A root item is represented by `FilterRootItem` and can be one of two kinds:

- root condition: `FilterRootItem.Condition`
- root group: `FilterRootItem.Group`

The visual root order in the settings window must follow `RootFilterItems` exactly. Formula generation must also follow this order.

## Current canonical operations

Use these operations for the filter editor:

- `DatasetConfig.AddRootFilterCondition()`
- `DatasetConfig.AddChildFilterGroup()`
- `DatasetConfig.RemoveRootFilterItem(...)`
- `DatasetConfig.RemoveFilterCondition(...)`
- `DatasetConfig.RootFilterItems.Move(...)`

## Compatibility collections

`DatasetConfig.FilterConditions` and `DatasetConfig.RootFilterGroup.Groups` are still kept for compatibility with XML loading and older internal code.

New UI code should not render root conditions and root groups as two separate lists. It should render `RootFilterItems` as one ordered list.

## XML import

`MscsXmlService.LoadFromMscsXml(...)` must call `DatasetConfig.EnsureRootFilterItems()` after parsing filter conditions. This keeps imported profiles ready for the unified filter editor before the settings window is opened.

## Legacy ViewModel commands

`MainWindowViewModel.AddConditionCommand`, `RemoveConditionCommand`, and the old manual `RebuildFilterFormulaFromConditions()` path are legacy. They should either delegate to `DatasetConfig` or be removed after confirming there are no remaining XAML bindings to them.

The current settings window uses code-behind handlers and `DatasetConfig` operations instead of these legacy commands.
