# Crossref schemas

Offline copy of the files under https://www.crossref.org/schemas/ needed to validate
Crossref 5.3.1 deposits, keeping Crossref's directory layout so relative imports resolve.
Downloaded on 2026-09-23. `CrossrefSchema` loads them as embedded resources.

`crossref5.3.1.xsd` and `common5.3.1.xsd` import MathML from `http://www.w3.org/Math/XMLSchema/mathml3/`;
`CrossrefSchema` maps that to the copy in `standard-modules/mathml3/`, which the JATS schema imports too.

To update, download the same files again from https://www.crossref.org/schemas/.
