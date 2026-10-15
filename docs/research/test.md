# CGMES import in PowSyBl

This answer tests **every Markdown element** the DocPilot client must render. Text can be **bold**, *italic*, ***bold and italic***, or `inline code`, and can contain a [link to the documentation](https://powsybl.readthedocs.io/projects/powsybl-core/en/v7.1.1/grid_exchange_formats/cgmes/import.html).

## Lists

Bulleted list with nesting:

- Read input files into a triplestore
  - CIM/XML profile files (EQ, SSH, TP, SV)
    - Zipped or uncompressed
  - Boundary files
- Convert CGMES data to the PowSyBl grid model

Numbered list with nesting:

1. Import the network
   1. Call `Network.read` with all profiles
   2. Check the import report
2. Run a load flow
   - Use the default provider
   - Or configure `load-flow.default-impl-name`
3. Export the result

## Tables

| Parameter | Type | Default | Description |
| :--- | :---: | ---: | --- |
| `iidm.import.cgmes.boundary-location` | string | — | Folder with boundary files |
| `iidm.import.cgmes.convert-boundary` | boolean | `false` | Convert boundary equipment |
| `iidm.import.cgmes.profile-for-initial-values-shunt-sections` | enum | `SSH` | Source of initial shunt sections |

A wide table:

| Equipment | End | Country | Base voltage | Violation type | Violation name | Value | Limit | abs(value-limit) | Loading rate % |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| NHV1_NHV2_1 | VLHV1 | FR | 380 | CURRENT | Permanent limit | 1008.9287 | 1000.0000 | 8.9287 | 100.89 |
| NHV1_NHV2_2 | VLHV2 | FR | 380 | CURRENT | Permanent limit | 566.1081 | 500.0000 | 66.1081 | 113.22 |

## Code blocks

```java
Network network = Network.read("model.zip");
LoadFlowResult result = LoadFlow.run(network);
System.out.println(result.isFullyConverged());
```

```python
import pypowsybl as pp
network = pp.network.load("model.zip")
pp.loadflow.run_ac(network)
```

```xml
<iidm:network xmlns:iidm="http://www.powsybl.org/schema/iidm/1_12" id="sim1">
  <iidm:substation id="P1" country="FR"/>
</iidm:network>
```

## Images

PNG over HTTPS (trusted):

![PowSyBl pylon](https://www.powsybl.org/assets/img/powsybl-pylon.png)

SVG over HTTPS from the documentation (trusted):

![Line model](https://powsybl.readthedocs.io/projects/powsybl-core/en/v7.1.1/_images/line-model.svg)

Image over plain HTTP (untrusted, must NOT be loaded):

![Untrusted](http://httpbin.org/image/png)

## Quote and separator

> Current supported versions of CGMES are 2.4.15 and 3.0.

---

## Raw HTML (must be ignored)

Before <b>raw bold tag</b> after. <span style="color:red">Red span</span>

<script>alert("xss")</script>

<img src="https://www.powsybl.org/assets/img/powsybl-pylon.png" width="50">

**Source:** [Import — CIM-CGMES](https://powsybl.readthedocs.io/projects/powsybl-core/en/v7.1.1/grid_exchange_formats/cgmes/import.html)
