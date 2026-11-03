package tbc.automation.pages;

import org.openqa.selenium.WebDriver;
import org.openqa.selenium.WebElement;

// Stub of LMSAutomationDemo's BasePage, with its signatures as checked on 2026-11-03: only what generated code may use.
public class BasePage {
    protected WebDriver winDriver;

    public BasePage(WebDriver winDriver) {
        this.winDriver = winDriver;
    }

    public WebElement waitForElementTobeVisible(WebElement element, int timeoutInSeconds) {
        return element;
    }

    public WebElement waitForEnabled(WebElement element, int timeoutSeconds) {
        return element;
    }
}