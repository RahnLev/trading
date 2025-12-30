# How to Export Tick Data from NinjaTrader for API Access

## Step-by-Step Instructions

### Option 1: Using NinjaTrader's Historical Data Manager (Easiest)

1. **Open NinjaTrader 8**

2. **Open Historical Data Manager**:
   - Go to: `Tools` → `Historical Data Manager`
   - Or press `Ctrl + Shift + H`

3. **Select Your Instrument**:
   - In the left panel, expand `Tick`
   - Find and select your instrument (e.g., `MNQ 03-26`)

4. **Select Date Range**:
   - Click on the date range you want to export
   - Or select multiple dates by holding `Ctrl` and clicking

5. **Export Data**:
   - Right-click on the selected data
   - Choose `Export`
   - Select location: `Documents\NinjaTrader 8\db\tick_export\`
   - Format: Choose `CSV` or `Text` format
   - Click `Export`

6. **Verify Export**:
   - Check that files are in: `c:\Mac\Home\Documents\NinjaTrader 8\db\tick_export\`
   - Files should be named like: `MNQ_03-26_tick_YYYYMMDD_HHMMSS.csv`

7. **Test API**:
   - The API will automatically detect exported CSV files
   - Try: `http://127.0.0.1:51888/api/data/download-tick?instrument=MNQ%2003-26&format=json`

### Option 2: Using the TickDataExporter AddOn (Advanced)

1. **Compile the AddOn**:
   - The `TickDataExporter.cs` AddOn should compile automatically when you compile NinjaScript
   - Go to: `Tools` → `Compile` (or press `F5`)
   - Check for any compilation errors

2. **Restart NinjaTrader**:
   - Close and reopen NinjaTrader to load the AddOn

3. **Use the AddOn Programmatically**:
   - The AddOn is now available but needs to be called from code
   - You can create a simple script or use it from another AddOn/Strategy

### Option 3: Manual CSV Export via Script (If Needed)

If you need to export programmatically, you can use NinjaTrader's built-in export functions in a script.

## File Locations

- **Source Data**: `c:\Mac\Home\Documents\NinjaTrader 8\db\tick\MNQ 03-26\*.ncd`
- **Exported CSV**: `c:\Mac\Home\Documents\NinjaTrader 8\db\tick_export\*.csv`
- **API Reads From**: `c:\Mac\Home\Documents\NinjaTrader 8\db\tick_export\*.csv`

## API Usage After Export

Once CSV files are exported, the API will automatically use them:

```
GET http://127.0.0.1:51888/api/data/download-tick?instrument=MNQ%2003-26&format=csv
GET http://127.0.0.1:51888/api/data/download-tick?instrument=MNQ%2003-26&start_date=2025-12-01&end_date=2025-12-28&format=json
```

## Troubleshooting

- **No files found**: Make sure you exported to the `tick_export` directory
- **API still shows .ncd error**: Check that CSV files exist in `db\tick_export\`
- **Export failed**: Ensure you have historical data downloaded in NinjaTrader first

