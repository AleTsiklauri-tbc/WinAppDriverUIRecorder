package tbc.automation.tests.loanUpdateOperations;

import ge.tbc.testautomation.utils.annotations.ZephyrScenario;
import org.testng.Assert;
import org.testng.annotations.Test;
import tbc.automation.BaseTest;
import tbc.automation.databases.B7Queries;
import tbc.automation.databases.LmsQueries;
import tbc.automation.steps.Landing.LandingSteps;
import tbc.automation.steps.Landing.clientCenter.clientSearch.ClientSearchSteps;
import tbc.automation.steps.Landing.clientCenter.loan.ChangeOfTermsSteps;
import tbc.automation.steps.Landing.clientCenter.loan.LoanSteps;
import tbc.automation.steps.Login.LoginSteps;
import tbc.automation.utils.Log;

import static tbc.automation.data.LoanStateData.HEALTHY_LOAN_STATE;

@ZephyrScenario("SQDLA-T637")
@Test(description = "Change of service account id for an active loan", groups = {"loanUpdateoperations", "Regression"})
public class ChangeOfTermsTest extends BaseTest {

    private static final Log log = Log.getLogger(ChangeOfTermsSteps.class);
    private static final long DB_WAIT_MILLIS = 8000L;

    private final B7Queries b7Queries = new B7Queries();
    private final LmsQueries lmsQueries = new LmsQueries();

    private Long loanId;
    private String initialAccountNumber;

    @Test(priority = 1)
    public void loginToApplication() {
        log.info("Logging into the application");
        LoginSteps loginSteps = new LoginSteps(winDriver);
        loginSteps.logIn();
    }

    @Test(priority = 2)
    public void getActiveLoan() {
        loanId = 0L; // sanitized: a real loan id that has an alternate account
       // loanId = lmsQueries.getRandomLoanBasedOnState(HEALTHY_LOAN_STATE);
        log.info("Loan ID: " + loanId);
    }

    @Test(priority = 3)
    public void selectLoanIdInLMSApplication() {
        LandingSteps landingSteps = new LandingSteps(winDriver);
        landingSteps.openClientsCenter();

        ClientSearchSteps clientSearchSteps = new ClientSearchSteps(winDriver);
        clientSearchSteps.searchByLoanID(String.valueOf(loanId));
    }

    @Test(priority = 4)
    public void selectAlternateAccountIdTest() {
        log.info("Select Alternate Account Id Test");

        initialAccountNumber = getBorrowerAccountIdFromDb(loanId);

        ChangeOfTermsSteps changeOfTermsSteps = new ChangeOfTermsSteps(winDriver);
        changeOfTermsSteps
                .clickChangeOfTermsButton()
                .clickAccountIdEditBox()
                .clearExistingAccountNumber()
                .clickSearchButton()
                .clickAlternateAccountInListView()
                .clickOKButton();

        validateDraftEventCreated(loanId);
    }

    @Test(priority = 5)
    public void authorizeLoanChangesTest() {
        authorizeLoanChanges();
    }

    @Test(priority = 6)
    public void validateAccountIdChangedInDB() {
        waitForDbUpdate();
        validateAccountIdChanged(initialAccountNumber, loanId);
    }

    private void validateDraftEventCreated(long loanId) {
        try {
            Thread.sleep(DB_WAIT_MILLIS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            Assert.fail("Interrupted while waiting for draft event", e);
        }
        b7Queries.validateDraftsEventNotNull(String.valueOf(loanId));
    }

    private void authorizeLoanChanges() {
        LoanSteps loanSteps = new LoanSteps(winDriver);
        loanSteps.moveToOperationsTab()
                .authorizeChanges()
                .confirmAuthorizeChanges();
    }

    private String getBorrowerAccountIdFromDb(long loanId) {
        String loanIdAsString = String.valueOf(loanId);
        String accountId = b7Queries.getColumnValueFromB7Table(
                "BorrowerAccId",
                "<database>.<schema>.Loans",
                loanIdAsString,
                "Id"
        );
        log.info("Account Id from DB for loan " + loanIdAsString + ": " + accountId);
        return accountId;
    }

    private void waitForDbUpdate() {
        try {
            Thread.sleep(DB_WAIT_MILLIS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            Assert.fail("Interrupted while waiting for DB update", e);
        }
    }

    private void validateAccountIdChanged(String initialAccountId, long loanId) {
        log.info("Validate Alternate Account Id Test");
        String updatedAccountId = getBorrowerAccountIdFromDb(loanId);
        log.info("Updated Account Id from DB: " + updatedAccountId);

        Assert.assertNotEquals(
                updatedAccountId,
                initialAccountId,
                "Error - Account Id has not changed."
        );
    }
}
