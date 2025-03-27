# Azure DevOps MCP Server Project Setup 🚀

![dotnet](https://github.com/aherrick/AzureDevOpsMCPServer/actions/workflows/dotnet.yml/badge.svg)

## Steps to Run the Project

1. **Clone the repository**:

```bash
git clone <repository-url>
cd <repository-name>
```

2. **Build the project**:

```bash
dotnet build
```

3. **Configure MCP Settings**:

Open your MCP settings file and update the configuration with your details:

```json
"azuredevops": {
    "command": "cmd",
    "args": [
        "/c",
        "<path-to-your-executable>\\AzureDevOpsMCPServer.exe"
    ],
    "env": {
        "AZURE_DEVOPS_ORG": "<your-organization>",
        "AZURE_DEVOPS_PAT": "<your-pat>",
        "AZURE_DEVOPS_PROJECT": "<your-project>"
    }
}
```
