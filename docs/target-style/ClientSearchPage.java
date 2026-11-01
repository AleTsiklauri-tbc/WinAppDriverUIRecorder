package tbc.automation.pages.Landing.clientCenter.clientSearch;

import io.appium.java_client.windows.WindowsDriver;
import org.openqa.selenium.WebElement;
import tbc.automation.pages.BasePage;

public class ClientSearchPage extends BasePage {
    private final WindowsDriver winDriver;

    public ClientSearchPage(WindowsDriver driver) {
        super(driver);
        this.winDriver = driver;
    }

    // Locators only - all public for access from Steps classes
    public WebElement getSearchByLoanIdTextBox(){return winDriver.findElementByAccessibilityId("loanIdEditBox");}
    public WebElement getSearchByClientNumberTextBox(){return winDriver.findElementByAccessibilityId("clientNoEditBox");}
    public WebElement getSearchButton(){return winDriver.findElementByAccessibilityId("searchTaskItem");}
}
