# Shiori v1.1.3.1

Shiori v1.1.3.1 aligns the MCP server version response with the shared
four-part version format.

## Changes

- Changes `get_version` to return the four-part assembly version, such as
  `1.1.3.1`, instead of the three-part informational version.
- Adds regression coverage for the server name and four-part version response.
