# FlowToVisio command line

Draws Power Automate cloud flows (and Logic Apps) into a Visio file, one page per flow, with the same engine,
shapes and layout as the XrmToolBox tool. No XrmToolBox, no connection and no telemetry: it reads flow
definitions from JSON files and writes a `.vsdx`.

## Build

```
dotnet build FlowToVisio.Cli -c Release
```

Needs the .NET 8 SDK or later. The engine sources are linked from `FlowToVisio/Visio` (not copied), and the
Visio template and `actions.json` are embedded in the executable.

## Use

```
FlowToVisio.Cli <file.json | folder>... -o flows.vsdx [-l list.txt] [-m manifest.json] [-a actions.json] [-d]
```

| Option | |
|---|---|
| `-o`, `--output` | Visio file to write. Required. |
| `-l`, `--list` | Text file with one input path per line; pages follow that order. |
| `-m`, `--manifest` | JSON with one entry per input: page number, name, source file, shapes drawn, unknown actions, error. |
| `-a`, `--actions` | `actions.json` to use instead of the embedded one. |
| `-d`, `--details` | Also show trigger conditions, concurrency, secure inputs/outputs, tracked properties and tracking id. |

Folders are read recursively (`*.json`), sorted by path. The exit code is 0 when every input was drawn.

Accepted input files:

- the definition as Dataverse keeps it in `workflow.clientdata`: `{"properties": {"definition": ..., "connectionReferences": ...}}`;
- an object with that definition in `clientdata` (as a string or an object) and, optionally, the page name in `_meta.name`;
- a flow or Logic App as the Power Automate or Azure APIs return it (`{"name", "properties": {"displayName", "definition", ...}}`).

Flow comments are drawn when the file has a `comments` array (`anchor`, `text`, `createdby`, `createdon`).

Set `FLOWTOVISIO_DEBUG=1` to print the stack trace of any action the engine cannot draw (it is drawn as a plain
action and listed as unknown in the manifest).

## Changes to the shared engine

They also apply to the XrmToolBox plugin:

- actions without `runAfter` (flows saved through the API or other tools) no longer stop the drawing of their
  scope, loop or condition;
- conditions written as expressions (`"expression": "@equals(...)"`) and nested `and`/`or` are shown in full;
- switches without a default case, and containers without actions, are drawn;
- Dataverse actions whose connection reference is not the first one (`shared_commondataserviceforapps-1`) are
  recognised;
- the Dataverse trigger shows the right scope and "run as" (it used the message names), and "When an action is
  performed" is drawn with its own template instead of failing;
- HTTP responses with a body that is not an object, or without a status code, are drawn;
- action types are matched without case (`foreach`);
- `actions.json`: the Teams templates after "Add Team Member" were nested inside it and never matched; new
  templates for Delay, Decrement variable, the Copilot Studio (Skills) trigger, Dataverse delete / update only /
  download file, When an action is performed, shared mailbox email, When a new email arrives (V3) and file system
  Create file.
