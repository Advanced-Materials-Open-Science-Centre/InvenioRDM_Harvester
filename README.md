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

The application is configured through a `config.json` file, which must reside in the same directory as the application executable. The configuration file adheres to the following JSON structure:

```json
{
  "ApiUrl": "https://dataset.pnu.edu.ua/",
  "AccessToken": "",
  "CrossRefUser": "",
  "CrossRefPassword": "",
  "CrossRefApiUrl": "https://test.crossref.org/servlet/deposit"
}
```

- `ApiUrl` - Invenio RDM host address
- `AccessToken` - Personal access token generated in Invenio RDM user's cabinet
- `CrossRefUser` - CrossReference account username
- `CrossRefPassword` - CrossReference account password
- `CrossRefApiUrl` - CrossReference XML submission endpoint. 


### Run

Provide one or more IDs of the publications from Invenio RDM site in order to start conversion process:

```bash
.\ConverterPoC.exe hae0c-y5202 dsvcx-mc604
```