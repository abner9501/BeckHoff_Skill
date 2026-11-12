---
name: tchmi-inputbox-grid
description: Add or modify recipe-backed parameter InputBoxes, Grid layouts, and Dashboard-style action button cards in TwinCAT HMI projects. Use when placing editable process parameters or changing Grid-based HMI content and button insets.
---

# TwinCAT HMI InputBox and Grid

## New PLC symbols

When a control uses a newly added PLC variable, also use the local `tchmi-symbol-mapping` skill to complete Map Symbol and verify the active HMI Server mapping. A `%s%...%/s%` control binding or successful HMI build alone does not establish that the PLC symbol is mapped and readable. Keep mapping and recipe membership separate; preserve an explicit user instruction to exclude a control from recipes.

## Recipe-backed parameters

Editable process-parameter InputBoxes, including setup-mode flow and pressure, must participate in the project's recipe. Inspect comparable controls and the active project's recipe management configuration first. Configure the InputBox recipe attributes using the existing project convention, then ensure the corresponding PLC or HMI symbol is included in the appropriate recipe type and recipe data as required by that project. A control attribute alone does not establish that the value is saved and restored.

Preserve existing recipes and values; do not overwrite historical parameter values or invent a machine-safe initial value. Determine appropriate defaults from existing project data or ask when they cannot be established safely. Verify symbol mapping, read/write behavior, recipe membership and the save/load path; report when runtime verification is unavailable. Do not add live status or read-only displays to recipes merely because they are InputBoxes.

## InputBox units

Trace the bound PLC value and its scaling before choosing a unit; the terminal model or numeric data type alone does not establish the engineering unit. Change the Plastic InputBox's own unit attributes instead of adding a separate unit Textblock. Preserve the symbol, bounds, precision, permissions and existing recipe behavior.

Set `DisplayUnitText=true`, the appropriate `UnitGroup`, and valid `SysUnit`/`DispUnit` IDs from the project's unit configuration. For a control that must keep a fixed display unit, set `CreateAutoBindings=false` so automatic unit bindings do not replace the explicit settings. For example, a value already in millimeters can use `UnitGroup=Length` and `SysUnit=DispUnit=Unit_Length_mm`. Preserve shared Configurator settings for controls intended to follow the user's unit selection.

`RefVal` is the reference corresponding to 100% when converting to or from percent; it is not a generic unit-display switch. For values already on a 0..100 percent scale, use `UnitGroup=Percent`, `SysUnit=DispUnit=Unit_Percent`, and `RefVal=100` to preserve their magnitude. A normalized 0..1 signal needs reference 1. For a physical position optionally displayed as percent, retain the appropriate axis reference rather than replacing it with 100.

The native unit span in this Plastic InputBox version renders after the numeric input and can extend beyond the control rectangle. Reserve space in the containing Grid columns and padding rather than sizing the input to fill the entire available width. Grid cell overflow can clip both a shifted input and its unit even when their bounds fit the card; if intentionally using reserved adjacent padding, inspect the generated cell wrapper and its row/column overflow settings, enable overflow only where needed, and check that neighboring content remains clear. One verified I/O layout uses a 110px value column, an 80px InputBox and `Right=26px` for `%`; recompute for longer units such as `mm`. Verify the actual unit span stays within the cell and card, with Chinese and English labels fitting. In an isolated preview that blocks PLC writes, check representative values to confirm the unit change has not rescaled them; a successful build alone does not prove correct runtime units.

## Grid layout

Before adding or moving controls in a Grid, inspect its row and column options, units, fixed and factor dimensions, spans, cell indexes, child offsets, alignment and neighboring controls. Identify the existing pattern of content rows and spacing rows, then add or adjust rows and columns according to that pattern rather than placing controls arbitrarily in a spacer. Keep labels and inputs aligned, avoid overlaps and clipping, and account for the supported viewport sizes. Preserve unrelated controls and behavior; verify the resulting layout in the HMI runtime or designer when available and report any verification gap.

Size text controls and their enclosing Grid cells and containers to fit the complete rendered text, including padding, in both Chinese and English. Adjust column or row allocation and, when necessary, the outer container dimensions so longer translations are not clipped or overlapped; use wrapping with sufficient height where appropriate. Check the longest localized text at supported display sizes in the runtime or designer when available, and report if visual verification was not possible.

## Dashboard-style action buttons

For this project's heating toggle cards and buttons requested in the same style, use Dashboard's `Home_AllHeating_On_Body_Grid` as the visual reference: a title above the button body, a centered icon button, rounded corners, and a state indicator behind the button. Preserve each button's PLC binding and keep the indicator behind it so the button remains clickable.

The user-specified sizing convention is a fixed **5px inset on every side** of the button body. Bind the inner button to the outer body's actual `RenderedWidth` and `RenderedHeight`, subtracting **10px total from each dimension**, and set `Left=5px`, `Top=5px`. Use pixel units for all four attributes. For example:

```html
data-tchmi-left="5" data-tchmi-left-unit="px"
data-tchmi-top="5" data-tchmi-top-unit="px"
data-tchmi-width="%f%%ctrl%Home_AllHeating_On_Body_Grid::RenderedWidth%/ctrl% - 10%/f%"
data-tchmi-width-unit="px"
data-tchmi-height="%f%%ctrl%Home_AllHeating_On_Body_Grid::RenderedHeight%/ctrl% - 10%/f%"
data-tchmi-height-unit="px"
```

Replace the control ID with the actual button body ID when reusing the pattern. Measure from the body below the title, not the full card or the button itself. Apply these offsets in a single cell covering the body; do not also add spacer rows/columns or padding that duplicates the 5px inset. If the reference markup uses older fixed rows, percentage sizes, or wider horizontal gaps, adapt it to this convention instead of copying those values. Confirm that resizing preserves 5px on all sides and that the state indicator stays centered around the button.

## Fixed-height pages and image layouts

When the user asks to display all content within a fixed height without scrolling, treat that height as a layout budget. Inspect the enclosing Region, content height, navigation rows, Grid rows/gaps and nested containers together. Reducing only the outer content height, hiding overflow or disabling scrolling is not a complete fix: fixed rows can consume the space needed by later sections and silently clip controls.

Read the image control's scripts and active layout configuration before resizing. TwinCAT Plastic `ZoneImageLayout` uses configured pixel positions, background dimensions and `LayoutWidth`/`LayoutHeight`; a smaller outer control does not automatically rescale its image or temperature inputs. Preserve PLC bindings, events, permissions and recipe values. For a page-specific presentation change, prefer a local canvas/container and scoped styles over changing shared layout recipes or vendor package scripts.

Scale the image, temperature inputs and related overlays such as fan indicators together with one uniform scale and a shared origin. Keep section titles readable. To use available space, measure the union of the visible artwork and all overlays rather than scaling large transparent image margins. Choose the largest uniform scale that fits both the available width and height, with space for titles and padding. If cropping is requested, retain all required controls, translate their coordinates consistently with the crop, and save a derived project image rather than overwriting a shared source image unnecessarily. Register new assets/styles in the project and resource configuration as appropriate. Do not distort the artwork with independent X/Y scaling merely to fill blank space.

For example, a 770px content can allocate 45px to navigation, 200px to each of two extruder sections, 285px to the head section and four 10px gaps. This is an example budget, not a mandatory layout or universal scale factor; recompute it for the actual content and requested size.

Verify in the runtime at the target viewport: inspect every section, the lowest/rightmost inputs, complete image bounds, readable Chinese/English text and overlay alignment. Confirm that the main Region has no scroll overflow (for example, `scrollHeight <= clientHeight` and `scrollWidth <= clientWidth`, allowing rounding), and that visible control bounds stay within the intended section. Absence of a scrollbar alone does not prove that content is complete. Preserve unsaved designer edits before refreshing externally changed files, reload stale designer/LiveView buffers when necessary, and verify the refreshed result rather than reporting a successful build as visual proof. Report any runtime verification gap explicitly.

## 适用环境

- Platform: Windows TwinCAT HMI engineering project.
- Dependencies: The existing project, its installed HMI controls, configuration and applicable TwinCAT HMI version.
- Validation: packaging and static checks dated 2026-11-12; no new device or HMI runtime verification.
- Boundary: preserve the original project assumptions; authorize device writes or configuration activation separately.
