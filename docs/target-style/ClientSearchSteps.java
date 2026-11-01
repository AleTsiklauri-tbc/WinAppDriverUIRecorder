package tbc.automation.steps.Landing.clientCenter.clientSearch;

import io.appium.java_client.windows.WindowsDriver;
import tbc.automation.pages.Landing.clientCenter.clientSearch.ClientSearchPage;
import tbc.automation.utils.Log;

public class ClientSearchSteps {
    private final WindowsDriver winDriver;
    private final ClientSearchPage clientSearchPage;
    private static final Log log = Log.getLogger(ClientSearchSteps.class);

    public ClientSearchSteps(WindowsDriver driver) {
        this.winDriver = driver;
        this.clientSearchPage = new ClientSearchPage(driver);
        log.info("ClientSearchSteps initialized");
    }

    public ClientSearchSteps searchByLoanID(String loanID){
        log.info("Searching by Loan ID: " + loanID);
        clientSearchPage.getSearchByLoanIdTextBox().sendKeys(loanID);
        log.info("Entered Loan ID in search box");
        clientSearchPage.getSearchButton().click();
        log.info("Clicked Search button for Loan ID search");
        return this;
    }

    public ClientSearchSteps searchByClientNumber(String clientNumber){
        log.info("Searching by Client Number: " + clientNumber);
        clientSearchPage.getSearchByClientNumberTextBox().sendKeys(clientNumber);
        log.info("Entered Client Number in search box");
        clientSearchPage.getSearchButton().click();
        log.info("Clicked Search button for Client Number search");
        return this;
    }
}
