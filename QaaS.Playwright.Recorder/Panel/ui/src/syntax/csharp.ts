import { type Token, type TokenKind, splitLines } from './token';

// A small C# tokenizer for the code the recorder writes: comments (/// ones as XML docs), strings (regular, verbatim,
// raw and interpolated, with the code in their holes), numbers, keywords, and names told apart as types, methods and
// members by where they are.

const keywords = new Set(
  ('abstract and as async await base bool break byte case catch char class const continue decimal default delegate do ' +
    'double else enum event explicit extern false file finally fixed float for foreach get global goto if implicit in ' +
    'init int interface internal is lock long nameof namespace new not null object operator or out override params ' +
    'partial private protected public readonly record ref required return sbyte sealed set short static string struct ' +
    'switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile when where ' +
    'while with yield').split(' '),
);

// The flow's own members read like types; they are not.
const members = new Set(['Configuration', 'BaseUrl']);

/** The code's tokens, line by line. */
export function tokenizeCSharp(code: string): Token[][] {
  return splitLines(scan(code));
}

function scan(code: string): Token[] {
  const tokens: Token[] = [];
  const push = (kind: TokenKind, text: string) => {
    if (text) tokens.push({ kind, text });
  };
  let i = 0;
  while (i < code.length) {
    const c = code[i];
    const next = code[i + 1];
    if (c === '/' && next === '/' && code[i + 2] === '/') {
      const end = endOfLine(code, i);
      scanDoc(code.slice(i, end), tokens);
      i = end;
    } else if (c === '/' && next === '/') {
      const end = endOfLine(code, i);
      push('com', code.slice(i, end));
      i = end;
    } else if (c === '/' && next === '*') {
      const end = code.indexOf('*/', i + 2);
      const stop = end < 0 ? code.length : end + 2;
      push('com', code.slice(i, stop));
      i = stop;
    } else if (/^[$@]*"/.test(code.slice(i, i + 4)) && (c === '"' || c === '$' || c === '@')) {
      i = scanString(code, i, tokens);
    } else if (c === "'") {
      let end = i + 1;
      while (end < code.length && code[end] !== "'" && code[end] !== '\n') end += code[end] === '\\' ? 2 : 1;
      push('str', code.slice(i, end + 1));
      i = end + 1;
    } else if (isDigit(c)) {
      const match = /^(0x[\da-f_]+|[\d_]*\.?\d+(e[+-]?\d+)?)[a-z]*/i.exec(code.slice(i))!;
      push('num', match[0]);
      i += match[0].length;
    } else if (isWordStart(c)) {
      let end = i + 1;
      while (end < code.length && isWord(code[end])) end++;
      const word = code.slice(i, end);
      push(classify(word, previousSignificant(tokens), nextSignificant(code, end)), word);
      i = end;
    } else if (/\s/.test(c)) {
      let end = i + 1;
      while (end < code.length && /\s/.test(code[end])) end++;
      push('plain', code.slice(i, end));
      i = end;
    } else {
      push('punc', c);
      i++;
    }
  }
  return tokens;
}

// A /// line: its text readable, its XML tags dimmed, and what a tag refers to in the colour of a type.
function scanDoc(line: string, tokens: Token[]): void {
  tokens.push({ kind: 'com', text: '///' });
  for (const part of line.slice(3).split(/(<[^<>]*>)/)) {
    if (!part) continue;
    if (!part.startsWith('<')) {
      tokens.push({ kind: 'doc', text: part });
      continue;
    }
    for (const piece of part.split(/("[^"]*")/)) {
      if (piece) tokens.push({ kind: piece.startsWith('"') ? 'docref' : 'doctag', text: piece });
    }
  }
}

// Words after which a name is a type whatever follows (a constructor, a declaration, an attribute), after which a
// capitalized one is, and after which a name is a member being declared.
const declaresType = new Set(['new', 'class', 'record', 'struct', 'interface', 'enum', '[']);
const beforeType = new Set([':', '<', ',', '(']);
const beforeMember = new Set(['string', 'bool', 'int', 'long', 'double', 'object', 'var', '>', ']']);

function classify(word: string, before: string, after: string): TokenKind {
  if (keywords.has(word) && before !== '.') return 'kw';
  if (members.has(word)) return 'prop';
  if (declaresType.has(before)) return 'type';
  if (after === '(' || after === '<(') return 'fn';
  if (beforeType.has(before) && /^[A-Z]/.test(word) && after !== '=') return 'type';
  // A member, read (dialog.Page) or set in an initializer (new() { Name = ... }), or declared (string Title).
  if (before === '.' || after === '=' || beforeMember.has(before)) return 'prop';
  return /^[A-Z]/.test(word) ? 'type' : 'plain';
}

// A string from its opening quote: "...", @"...", $"...", or a raw """...""", with the code in an interpolated one's
// holes tokenized as code.
function scanString(code: string, start: number, tokens: Token[]): number {
  let i = start;
  let verbatim = false;
  let dollars = 0;
  while (code[i] !== '"') {
    if (code[i] === '@') verbatim = true;
    if (code[i] === '$') dollars++;
    i++;
  }
  let quotes = 0;
  while (code[i + quotes] === '"') quotes++;
  const raw = quotes >= 3;
  if (!raw) quotes = 1;
  // An empty string, "" or $"", is two quotes and no raw string.
  if (!raw && code[i + 1] === '"' && !verbatim) {
    tokens.push({ kind: 'str', text: code.slice(start, i + 2) });
    return i + 2;
  }
  i += quotes;
  const close = '"'.repeat(quotes);
  // The braces that open a hole: one in "$...", as many as the dollars in a raw string.
  const holeBraces = raw ? Math.max(dollars, 1) : 1;
  const interpolated = dollars > 0;
  let text = code.slice(start, i);
  const flush = () => {
    if (text) tokens.push({ kind: 'str', text });
    text = '';
  };
  while (i < code.length) {
    const c = code[i];
    if (!verbatim && !raw && c === '\\') {
      text += code.slice(i, i + 2);
      i += 2;
    } else if (raw && code.startsWith(close, i)) {
      text += close;
      i += quotes;
      break;
    } else if (!raw && c === '"' && verbatim && code[i + 1] === '"') {
      text += '""';
      i += 2;
    } else if (!raw && c === '"') {
      text += '"';
      i++;
      break;
    } else if (interpolated && !raw && (c === '{' || c === '}') && code[i + 1] === c) {
      text += c + c;
      i += 2;
    } else if (interpolated && code.startsWith('{'.repeat(holeBraces), i) && code[i + holeBraces] !== '{') {
      flush();
      const open = i + holeBraces - 1;
      const end = closingBrace(code, open);
      tokens.push({ kind: 'punc', text: '{'.repeat(holeBraces) });
      tokens.push(...scan(code.slice(open + 1, end)));
      if (end < code.length) tokens.push({ kind: 'punc', text: '}'.repeat(holeBraces) });
      i = end + holeBraces;
    } else if (c === '\n' && !verbatim && !raw) {
      break;
    } else {
      text += c;
      i++;
    }
  }
  flush();
  return i;
}

function closingBrace(code: string, open: number): number {
  let depth = 0;
  for (let i = open; i < code.length; i++) {
    if (code[i] === '"') {
      i++;
      while (i < code.length && code[i] !== '"') i += code[i] === '\\' ? 2 : 1;
    } else if (code[i] === '{') depth++;
    else if (code[i] === '}' && --depth === 0) return i;
  }
  return code.length;
}

function previousSignificant(tokens: Token[]): string {
  for (let i = tokens.length - 1; i >= 0; i--) if (tokens[i].kind !== 'plain' || tokens[i].text.trim()) return tokens[i].text;
  return '';
}

// The character after a name, past spaces; '<(' for a generic method's call, e.g. Array.Empty<string>().
function nextSignificant(code: string, from: number): string {
  let i = from;
  while (i < code.length && (code[i] === ' ' || code[i] === '\t')) i++;
  if (code[i] === '=' && code[i + 1] === '=') return '==';
  if (code[i] === '<') {
    const close = /^<[\w\s,.<>?[\]]*?>(?=\()/.exec(code.slice(i, i + 120));
    if (close && balanced(close[0])) return '<(';
  }
  return code[i] ?? '';
}

const balanced = (generic: string) => [...generic].reduce((depth, c) => depth + (c === '<' ? 1 : c === '>' ? -1 : 0), 0) === 0;
const endOfLine = (code: string, from: number) => {
  const end = code.indexOf('\n', from);
  return end < 0 ? code.length : end;
};
const isDigit = (c: string) => c >= '0' && c <= '9';
const isWordStart = (c: string) => /[A-Za-z_]/.test(c);
const isWord = (c: string) => /\w/.test(c);
