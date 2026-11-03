package tbc.automation.config;

// Stub of LMSAutomationDemo's ConfigReader: environment variable first, then config.properties (docs/target-style/README.md).
public final class ConfigReader {
    private ConfigReader() {
    }

    public static String getProperty(String key) {
        return System.getenv(key);
    }
}
