The shared DMC catalog is `rgb-dmc.json`. RGB channels are authoritative for display colors.

On 2026-09-07, DMC 1–35 were added from the user-supplied `sources/MissingColors.xlsx`. Names come from column B; RGB channels come directly from the solid fill of column C (the first `FF` byte is opacity). The JSON `row` field identifies the source worksheet row.

The workbook also lists Ecru, Blanc^, White^, and B5200. These colors already existed, so their catalog names and RGB values were retained. Blanc is an alias for White, not an additional color. Numeric codes are stored without leading zeros; imports such as `05` use the same entry as `5`.

Run the normal server `--migrate` release command to refresh the database's shared catalog. This does not add these threads to anyone's owned inventory.
