## Invenio RDM to CrossRef Transfer tool

InvenioRDM Harvester can perform fully automatic transfer of Invenio RDM (https://inveniordm.web.cern.ch/) publications to CrossRef (https://www.crossref.org/) infrastructure. 

The automated transfer process involves the following key stages:

1.  **Data Retrieval:** Extraction of publication metadata from Invenio RDM in JSON format, utilizing a specified record identifier.
2.  **CrossRef XML Generation:** Transformation of the retrieved JSON data into a valid CrossRef XML document adhering to the CrossRef schema.
3.  **Submission:** Secure upload of the generated XML document to the designated CrossRef XML submission endpoint.

**Note:** This project is currently in its early stages of development, and while functional, users may encounter issues during the XML generation phase. Contributions and feedback are welcome.

### Prerequisites

* .NET Core SDK 9 or a later compatible version. Installation instructions can be found at: [https://dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)

### Build Instructions

1.  Navigate to the source directory:
    ```bash
    cd src
    ```
2.  Build the project using the .NET CLI:
    ```bash
    dotnet build
    ```

### Configuration

The application reads `config.json` from its working directory. `config.json` is tracked by git and only holds defaults, so put credentials and the records to deposit in `config.local.json` next to it: it is ignored by git, copied to the build output, and any setting in it overrides `config.json`.

```json
{
  "ApiUrl": "https://dataset.cnu.edu.ua/",
  "AccessToken": "",
  "CrossRefUser": "",
  "CrossRefPassword": "",
  "CrossRefApiUrl": "https://test.crossref.org/servlet/deposit",
  "DepositorName": "",
  "DepositorEmail": "",
  "Registrant": "",
  "ResultTimeoutMinutes": 5,
  "DoiMappings": [
    { "Doi": "10.xxxxx/example", "DepositoryRecordId": "abcde-12345" }
  ]
}
```

- `ApiUrl` - Invenio RDM host address
- `AccessToken` - Personal access token generated in Invenio RDM user's cabinet
- `CrossRefUser` - CrossReference account username
- `CrossRefPassword` - CrossReference account password
- `CrossRefApiUrl` - CrossReference XML submission endpoint (`https://doi.crossref.org/servlet/deposit` for production)
- `DepositorName` - Name of the organization or person submitting the deposit
- `DepositorEmail` - E-mail address CrossRef sends deposit success and error reports to
- `Registrant` - Organization that owns the registered content
- `ResultTimeoutMinutes` - How long to wait for CrossRef to process the deposits (`0` to skip waiting)
- `DoiMappings` - The DOI to register for each Invenio RDM record id


### Run

```bash
.\ConverterPoC.exe
```

For each entry in `DoiMappings` the tool:

1. Loads the record from Invenio RDM and saves it as `<record id>.json`.
2. Checks the DOI: the record must not already list a different DOI under the same prefix, and the DOI must not be registered in CrossRef for another record. DOIs from other prefixes (e.g. Zenodo) only produce a warning.
3. Converts the record to `<record id>.xml` and validates it against the bundled CrossRef 5.3.1 schema (`src/ConverterPoC/Schemas`).
4. Uploads it to CrossRef under a unique file name.

A record that fails any step is reported and skipped. The tool then waits up to `ResultTimeoutMinutes` for CrossRef's deposit results, prints them, and exits with code `1` if any record failed.