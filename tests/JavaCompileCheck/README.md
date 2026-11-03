# Java compile check

Spec 14, Phase 4 acceptance: the Java the recorder generates compiles against the framework it is written for.

- `stubs/` mirrors the signatures of the LMSAutomationDemo classes that generated code uses (`BasePage`, `BaseTest`,
  `Log`, `ConfigReader`, `GridSteps`, `Utility`), in the same packages, as checked against the project on 2026-11-03
  (ARCHITECTURE D-65). The bodies do nothing: only the signatures matter.
- `check-java.ps1 -Source <folder>` compiles every `.java` file in the folder together with the stubs, against the
  real jars (D-10). Exit codes: 0 compiled, 1 compile errors, 2 skipped (no JDK), 3 skipped (a jar is missing).
- `JavaCompileCheck.cs` runs the script from tests. `JavaCompileTests` (trait `Requires=JDK`) compiles the code
  generated for the acceptance flow and for every step kind; the Phase 3 end-to-end test compiles the code generated
  from the recording it makes.

## What it needs

A JDK 17 or later: `JAVA_HOME`, `javac` on `PATH`, or a per-user JDK in `%LOCALAPPDATA%\Microsoft\jdk-17`.

These jars, either in `lib/` here (ignored by git) or in the local Maven repository (`~/.m2/repository`), where a
build of LMSAutomationDemo puts them:

| Jar | Maven coordinates |
|---|---|
| java-client-7.6.0.jar | io.appium:java-client:7.6.0 |
| selenium-api-3.141.59.jar | org.seleniumhq.selenium:selenium-api:3.141.59 |
| selenium-remote-driver-3.141.59.jar | org.seleniumhq.selenium:selenium-remote-driver:3.141.59 |
| selenium-support-3.141.59.jar | org.seleniumhq.selenium:selenium-support:3.141.59 |
| guava-25.0-jre.jar | com.google.guava:guava:25.0-jre |
| testng-7.11.0.jar | org.testng:testng:7.11.0 |

Without them the tests are reported as skipped, never as passed.
