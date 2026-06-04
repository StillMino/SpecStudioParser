# Filter condition group editor integration

This step starts connecting the group model from PR #7 to datasets.

## Added here

- `DatasetConfig.RootFilterGroup` stores the root logical group for the dataset.
- The existing flat condition list remains in place for UI stability.
- The current condition editor can keep working while the group editor is introduced incrementally.

## Why this is separate

`MainWindowViewModel` is currently large and handles scanning, XML profile management, filtering, specification generation, and settings UI commands in one class. Replacing the flat condition editor with a nested group editor should be done in small increments to avoid destabilizing nanoCAD-hosted Avalonia windows.

## Next UI step

The next change should add a visible root-group selector to the filter tab:

```text
Join current conditions by: [and/or]
```

After that, `RebuildFilterFormulaFromConditions()` should delegate to `FilterFormulaBuilder` instead of string-concatenating conditions directly.

## Later steps

- Add `Add group` button.
- Render child groups.
- Move conditions between groups.
- Add safe row deletion.
- Parse imported formulas into a full condition tree where possible.
