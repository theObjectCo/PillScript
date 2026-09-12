#!/usr/bin/env node
'use strict';

// An MCP server over stdio that forwards to the bridge inside Rhino. It is a separate process on
// purpose: the tools stay listed whether or not Rhino is running, and a call made while it is
// closed answers with a sentence saying so rather than the whole server failing to connect.

const http = require('http');

const PORT = Number(process.env.PILLSCRIPT_BRIDGE_PORT) || 57321;
const PROTOCOL = '2024-11-05';

const COMPONENT = {
  type: 'string',
  description:
    'Instance id of the script component, or a unique prefix of it. May be left out when the ' +
    'canvas holds exactly one script component.'
};

const TOOLS = [
  {
    name: 'list_components',
    description:
      'Every C# Script component on the open Grasshopper canvases, with its id, nickname, ' +
      'document, whether its build is out of date, its files and its current parameters. Start ' +
      'here to find the component to work on.',
    inputSchema: { type: 'object', properties: {} }
  },
  {
    name: 'list_files',
    description: 'The files that make up one component\'s project, including Script.csproj.',
    inputSchema: { type: 'object', properties: { component: COMPONENT } }
  },
  {
    name: 'read_file',
    description: 'The contents of one file in the component\'s project.',
    inputSchema: {
      type: 'object',
      properties: { component: COMPONENT, file: { type: 'string', description: 'File name, e.g. Script.cs' } },
      required: ['file']
    }
  },
  {
    name: 'write_file',
    description:
      'Replaces a file in the component\'s project, creating it when it is not there yet. The ' +
      'whole content is written; there is no partial edit. Writing does not compile: call ' +
      'compile afterwards to build and get the diagnostics.',
    inputSchema: {
      type: 'object',
      properties: {
        component: COMPONENT,
        file: { type: 'string', description: 'File name. A new name must end in .cs' },
        content: { type: 'string', description: 'The complete new contents of the file.' }
      },
      required: ['file', 'content']
    }
  },
  {
    name: 'delete_file',
    description: 'Removes a file. Script.cs and Script.csproj cannot be removed.',
    inputSchema: {
      type: 'object',
      properties: { component: COMPONENT, file: { type: 'string' } },
      required: ['file']
    }
  },
  {
    name: 'rename_file',
    description: 'Renames a file. Script.cs and Script.csproj cannot be renamed.',
    inputSchema: {
      type: 'object',
      properties: { component: COMPONENT, from: { type: 'string' }, to: { type: 'string' } },
      required: ['from', 'to']
    }
  },
  {
    name: 'list_references',
    description:
      "The references in the component's project: assemblies pointed at by path, and NuGet " +
      'packages. Rhino and Grasshopper are not listed; they are always available.',
    inputSchema: { type: 'object', properties: { component: COMPONENT } }
  },
  {
    name: 'add_package',
    description:
      'Adds a NuGet package to the project, or changes the version of one already there. It is ' +
      'restored on the next compile, which needs the .NET SDK.',
    inputSchema: {
      type: 'object',
      properties: {
        component: COMPONENT,
        id: { type: 'string', description: 'Package id, e.g. MathNet.Numerics' },
        version: { type: 'string', description: 'Version. Left out means any.' }
      },
      required: ['id']
    }
  },
  {
    name: 'remove_package',
    description: 'Removes a NuGet package from the project.',
    inputSchema: {
      type: 'object',
      properties: { component: COMPONENT, id: { type: 'string' } },
      required: ['id']
    }
  },
  {
    name: 'add_reference',
    description: 'References an assembly by path, for a DLL that is not on NuGet.',
    inputSchema: {
      type: 'object',
      properties: { component: COMPONENT, path: { type: 'string', description: 'Full path to a .dll' } },
      required: ['path']
    }
  },
  {
    name: 'remove_reference',
    description: 'Drops an assembly reference, named as list_references reports it.',
    inputSchema: {
      type: 'object',
      properties: { component: COMPONENT, name: { type: 'string' } },
      required: ['name']
    }
  },
  {
    name: 'compile',
    description:
      'Builds the component and answers with the diagnostics and the parameters the build ' +
      'produced. The inputs and outputs come from the RunScript signature, so they change here.',
    inputSchema: { type: 'object', properties: { component: COMPONENT } }
  },
  {
    name: 'solve',
    description:
      'Recomputes the component with the build it already has, and answers with its parameters ' +
      'and whatever the script printed to out.',
    inputSchema: { type: 'object', properties: { component: COMPONENT } }
  },
  {
    name: 'open_editor',
    description: 'Opens the component\'s editor window, which also marks it blue on the canvas.',
    inputSchema: { type: 'object', properties: { component: COMPONENT } }
  }
];

function call(tool, args) {
  return new Promise(resolve => {
    const body = JSON.stringify({ tool: tool, arguments: args || {} });

    const request = http.request(
      {
        host: '127.0.0.1',
        port: PORT,
        path: '/',
        method: 'POST',
        headers: { 'content-type': 'application/json', 'content-length': Buffer.byteLength(body) }
      },
      response => {
        let text = '';
        response.on('data', chunk => { text += chunk; });
        response.on('end', () => resolve(text));
      }
    );

    request.on('error', error => {
      resolve(JSON.stringify({
        error:
          'The Grasshopper bridge did not answer on port ' + PORT + ' (' + error.code + '). ' +
          'The bridge is off unless asked for: set PILLSCRIPT_BRIDGE=on in the environment ' +
          'Rhino starts from, then start Rhino and open Grasshopper with PillScript installed.'
      }));
    });

    request.write(body);
    request.end();
  });
}

function send(message) {
  process.stdout.write(JSON.stringify(message) + '\n');
}

function reply(id, result) {
  send({ jsonrpc: '2.0', id: id, result: result });
}

async function handle(message) {
  const { id, method, params } = message;

  if (method === 'initialize') {
    reply(id, {
      protocolVersion: PROTOCOL,
      capabilities: { tools: {} },
      serverInfo: { name: 'pillscript', version: '0.1.0' }
    });
    return;
  }

  if (method === 'tools/list') {
    reply(id, { tools: TOOLS });
    return;
  }

  if (method === 'tools/call') {
    const text = await call(params && params.name, params && params.arguments);
    let failed = false;

    try {
      failed = Boolean(JSON.parse(text).error);
    } catch (error) {
      failed = true;
    }

    reply(id, { content: [{ type: 'text', text: text }], isError: failed });
    return;
  }

  if (method === 'ping') {
    reply(id, {});
    return;
  }

  // Notifications carry no id and want no answer.
  if (id === undefined || id === null) return;

  send({ jsonrpc: '2.0', id: id, error: { code: -32601, message: 'Unknown method: ' + method } });
}

let buffer = '';
let inFlight = 0;
let ended = false;

function settle() {
  inFlight--;
  if (ended && inFlight === 0) process.exit(0);
}

process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => {
  buffer += chunk;

  let newline;
  while ((newline = buffer.indexOf('\n')) >= 0) {
    const line = buffer.slice(0, newline).trim();
    buffer = buffer.slice(newline + 1);

    if (!line) continue;

    try {
      inFlight++;
      handle(JSON.parse(line)).then(settle, settle);
    } catch (error) {
      // A line that is not a message is not something to answer.
      inFlight--;
    }
  }
});

// Waits for calls that are still with Rhino, so a request is never dropped on the way out.
process.stdin.on('end', () => {
  ended = true;
  if (inFlight === 0) process.exit(0);
});
