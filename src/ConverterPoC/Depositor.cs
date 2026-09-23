namespace ConverterPoC;

// Values for the Crossref <head> element. Crossref emails deposit results to Email.
public record Depositor(string Name, string Email, string Registrant);

// Deposit-wide values that don't come from the record. RepositoryName is the InvenioRDM
// repository's own name: the title of the Crossref database that datasets belong to, and the
// default publisher of records, which is therefore not a journal title.
public record ConversionSettings(Depositor Depositor, string RepositoryName);
