package tbc.automation.steps;

import io.appium.java_client.windows.WindowsDriver;

// Stub of LMSAutomationDemo's GridSteps: a cell by 0-based row and its column's header, or the column's index for a
// column without a header (docs/target-style/README.md, D-12, D-54).
public class GridSteps {
    public GridSteps(WindowsDriver driver) {
    }

    public GridSteps clickCell(int rowIndex, String headerName) {
        return this;
    }

    public GridSteps clickCell(int rowIndex, int columnIndex) {
        return this;
    }
}
