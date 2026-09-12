// Monaco ships a C# tokenizer that emits only keyword, identifier, string, number and comment,
// so types, calls and control flow all come out the same colour. This one splits those apart,
// which is what makes the editor read like Visual Studio Code rather than like a plain textarea.

window.csharpLanguage = (function () {
  var keywords = [
    'abstract', 'as', 'base', 'bool', 'byte', 'char', 'checked', 'class', 'const', 'decimal',
    'delegate', 'double', 'enum', 'event', 'explicit', 'extern', 'false', 'fixed', 'float',
    'implicit', 'in', 'int', 'interface', 'internal', 'is', 'long', 'namespace', 'new', 'nint',
    'notnull', 'nuint', 'null', 'object', 'operator', 'out', 'override', 'params', 'partial',
    'private', 'protected', 'public', 'readonly', 'record', 'ref', 'required', 'sbyte', 'sealed',
    'short', 'sizeof', 'stackalloc', 'static', 'string', 'struct', 'this', 'true', 'typeof',
    'uint', 'ulong', 'unchecked', 'unsafe', 'ushort', 'var', 'virtual', 'void', 'volatile',
    'add', 'alias', 'ascending', 'async', 'by', 'descending', 'dynamic', 'equals', 'get',
    'global', 'init', 'nameof', 'on', 'remove', 'set', 'value', 'where', 'with'
  ];

  var controlKeywords = [
    'await', 'break', 'case', 'catch', 'continue', 'default', 'do', 'else', 'finally', 'for',
    'foreach', 'from', 'goto', 'group', 'if', 'into', 'join', 'let', 'lock', 'orderby', 'return',
    'select', 'switch', 'throw', 'try', 'using', 'when', 'while', 'yield'
  ];

  var operators = [
    '=', '??=', '||', '&&', '|', '^', '&', '==', '!=', '<=', '>=', '<<', '+', '-', '*', '/', '%',
    '!', '~', '++', '--', '+=', '-=', '*=', '/=', '%=', '&=', '|=', '^=', '<<=', '>>=', '>>', '=>',
    '??', '?.', '?'
  ];

  return {
    id: 'csharpx',

    configuration: {
      comments: { lineComment: '//', blockComment: ['/*', '*/'] },
      brackets: [['{', '}'], ['[', ']'], ['(', ')']],
      autoClosingPairs: [
        { open: '{', close: '}' },
        { open: '[', close: ']' },
        { open: '(', close: ')' },
        { open: '"', close: '"', notIn: ['string', 'comment'] },
        { open: "'", close: "'", notIn: ['string', 'comment'] }
      ],
      surroundingPairs: [
        { open: '{', close: '}' },
        { open: '[', close: ']' },
        { open: '(', close: ')' },
        { open: '"', close: '"' },
        { open: "'", close: "'" }
      ],
      folding: {
        markers: {
          start: new RegExp('^\s*#region\b'),
          end: new RegExp('^\s*#endregion\b')
        }
      },
      indentationRules: {
        increaseIndentPattern: new RegExp('^.*\{[^}"\']*$'),
        decreaseIndentPattern: new RegExp('^\s*\}')
      }
    },

    language: {
      defaultToken: '',
      tokenPostfix: '.cs',
      keywords: keywords,
      controlKeywords: controlKeywords,
      operators: operators,
      symbols: /[=><!~?:&|+\-*\/\^%]+/,

      tokenizer: {
        root: [
          [/^\s*#\w+/, 'keyword.directive'],

          [/\/\/\/.*$/, 'comment.doc'],
          [/\/\/.*$/, 'comment'],
          [/\/\*/, 'comment', '@blockComment'],

          // An attribute list, so [Description(...)] reads as a type and not as a call.
          [/^\s*\[(?=\s*[A-Za-z_])/, { token: 'delimiter.square', next: '@attribute' }],

          // A type name right after new, so a constructor call stays a type and not a call.
          [/(?<=\bnew\s+)[A-Za-z_]\w*/, 'type.identifier'],

          // An identifier followed by an argument list is a call, unless it is a keyword.
          [/[A-Za-z_]\w*(?=\s*(<[\w\s,\.\[\]<>]*>)?\s*\()/, {
            cases: {
              '@controlKeywords': 'keyword.control',
              '@keywords': 'keyword',
              '@default': 'method'
            }
          }],

          [/@?[A-Za-z_]\w*/, {
            cases: {
              '@controlKeywords': 'keyword.control',
              '@keywords': 'keyword',
              '~[A-Z].*': 'type.identifier',
              '@default': 'identifier'
            }
          }],

          { include: '@whitespace' },

          [/[{}()\[\]]/, '@brackets'],
          [/[<>](?!@symbols)/, 'delimiter.angle'],
          [/@symbols/, { cases: { '@operators': 'operator', '@default': '' } }],

          [/\d[\d_]*\.\d[\d_]*([eE][\-+]?\d+)?[fFdDmM]?/, 'number.float'],
          [/0[xX][0-9a-fA-F_]+[uUlL]*/, 'number.hex'],
          [/0[bB][01_]+[uUlL]*/, 'number.hex'],
          [/\d[\d_]*[fFdDmMuUlL]*/, 'number'],

          [/[;,.]/, 'delimiter'],

          [/"""/, 'string.quote', '@rawString'],
          [/[\$@]{1,2}"/, 'string.quote', '@verbatimString'],
          [/"/, 'string.quote', '@string'],
          [/'[^\']'/, 'string'],
          [/'\.'/, 'string.escape'],
          [/'/, 'string.invalid']
        ],

        whitespace: [
          [/[ \t\r\n]+/, '']
        ],

        // The name of an attribute, and any further attribute that follows it on the same line.
        attribute: [
          [/[ \t\r\n]+/, ''],
          [/[A-Za-z_]\w*/, 'annotation'],
          [/\./, 'delimiter'],
          [/\(/, { token: 'delimiter.parenthesis', next: '@attributeArguments' }],
          [/\](?=\s*\[)/, 'delimiter.square'],
          [/\[/, 'delimiter.square'],
          [/\]/, { token: 'delimiter.square', next: '@pop' }],
          [/./, 'delimiter']
        ],

        attributeArguments: [
          [/\)/, { token: 'delimiter.parenthesis', next: '@pop' }],
          [/[ \t\r\n]+/, ''],
          [/[\$@]{1,2}"/, 'string.quote', '@verbatimString'],
          [/"/, 'string.quote', '@string'],
          [/\d[\d_]*\.\d[\d_]*[fFdDmM]?/, 'number.float'],
          [/\d[\d_]*[fFdDmMuUlL]*/, 'number'],
          [/[A-Za-z_]\w*/, {
            cases: {
              '@keywords': 'keyword',
              '~[A-Z].*': 'type.identifier',
              '@default': 'identifier'
            }
          }],
          [/[,.=]/, 'delimiter'],
          [/./, 'delimiter']
        ],

        blockComment: [
          [/[^\/*]+/, 'comment'],
          [/\*\//, 'comment', '@pop'],
          [/[\/*]/, 'comment']
        ],

        string: [
          [/[^\\"]+/, 'string'],
          [/\u[0-9a-fA-F]{4}/, 'string.escape'],
          [/\./, 'string.escape'],
          [/"/, 'string.quote', '@pop'],
          [/$/, 'string.invalid', '@pop']
        ],

        // A verbatim string runs across lines and doubles its quotes to escape them.
        verbatimString: [
          [/[^"]+/, 'string'],
          [/""/, 'string.escape'],
          [/"/, 'string.quote', '@pop']
        ],

        rawString: [
          [/"""/, 'string.quote', '@pop'],
          [/[^"]+/, 'string'],
          [/"/, 'string']
        ]
      }
    }
  };
})();
