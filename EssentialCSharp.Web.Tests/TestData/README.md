# Test Data Directory

This directory contains test fixtures used by the test suite to ensure tests are isolated and independent of production data.

## Structure

```
TestData/
└── ListingSourceCode/
    └── src/
        ├── Chapter01/
        │   ├── 01.01.cs
        │   ├── 01.02.xml
        │   └── 01.03.cs
        └── Chapter10/
            ├── 10.01.cs
            ├── 10.02.cs
            └── Employee.cs  # Non-listing file to test filtering
```

## Purpose

Test files in this directory:
- Provide controlled, predictable test data
- Isolate tests from changes to production listing files
- Enable testing of edge cases and error conditions
- Are minimal in size for fast test execution
- Are automatically copied to the output directory during build

## File Naming Convention

Listing files follow the pattern: `{CC}.{LL}.{ext}`
- `CC`: Two-digit chapter number (e.g., "01", "10")
- `LL`: Two-digit listing number (e.g., "01", "15")
- `ext`: File extension (e.g., "cs", "xml")

Files not matching this pattern (like `Employee.cs`) are used to test that the service correctly excludes non-listing files.

## Build Configuration

These files are:
- Excluded from compilation via `<Compile Remove="TestData/**" />`
- Included as content via `<Content Include="TestData/**">`
- Copied to output directory with `CopyToOutputDirectory.PreserveNewest`

See [EssentialCSharp.Web.Tests.csproj](../EssentialCSharp.Web.Tests.csproj) for the full build configuration.
