# EDMCP
MCP Server for Easy Diffusion

This project implements a Model Context Protocol (MCP) server that integrates with Easy Diffusion to provide image generation capabilities via MCP-compliant clients like LM Studio.

## Quick Start

### Launching the EDMCP Server
Unzip EDMCP_server.zip and run start-edmcp.bat.  

This will start the EDMCP server listening on port 5242.

### Configuring LM Studio

1. Open LM Studio and navigate to Settings and then the Program tab.
2. Click Install then **"Edit mcp.json***
3. Fill in these details:
   - **Name**: `EDMCP` (or whatever you want)
   - **URL**: `http://localhost:5242/mcp`
   
   If your MCP server is on a different machine than LM Studio:
   - **URL**: `http://<your-server-ip-or-machine-name>:5242/mcp`

   It should look something like this:
   ```json
   {                
     "mcpServers": {
       "ed-mcp-server":{
	     "url": "http://localhost:5242/mcp"
       }  
     }
   }
   ```

  If you have other details in mcp.json, take care to maintain the proper syntax and not break it.  Best to save a copy before editing.

3. Click **"Save"**

### Networking
Currently, start-edmcp.bat is configured to listen to any machine on the local network.  Change ASPNETCORE_URLS to `http://localhost:5242` in the .bat file to restrict to local machine only.

## Security Notes
- This server does not implement authentication or encryption. Use within trusted networks only.
- Consider running behind a reverse proxy for added security in production environments
- Images are not stored by the MCP server; the client is responsible for handling received images, and will probably cache them locally.  Somewhere.
