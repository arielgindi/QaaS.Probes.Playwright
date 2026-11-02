import { type Token, type TokenKind, splitLines } from './token';

// A small C# tokenizer for the code the recorder writes: comments, strings (regular, verbatim and interpolated, with
// the code in their holes), numbers, keywords, and names told apart as types, methods and members by where they are.

const keywords = new Set(
  ('abstract as async await base bool break byte case catch char class const continue decimal default delegate do ' +
    'double else enum event explicit extern false finally fixed float for foreach get goto if implicit in init int ' +
    'interface internal is lock long namespace new null object operator out override params private protected public ' +
    'readonly record ref return sbyte sealed short static string struct switch this throw true try typeof uint ulong ' +
    'unchecked unsafe ushort using var virtual void volatile when where while').split(' '),
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
    if (c === '/' && next === '/') {
      const end = endOfLine(code, i);
      push('com', code.slice(i, end));
      i = end;
    } else if (c === '/' && next === '*') {
      const end = code.indexOf('*/', i + 2);
      const stop = end < 0 ? code.length : end + 2;
      push('com', code.slice(i, stop));
      i = stop;
    } else if (c === '"' || ((c === '$' || c === '@') && (next === '"' || ((next === '$' || next === '@') && code[i + 2] === '"')))) {
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

// Words after which a name is a type, and after which it is a member being declared.
const beforeType = new Set(['new', 'class', 'record', 'struct', 'interface', 'enum', ':', '<', ',', '(']);
const beforeMember = new Set(['string', 'bool', 'int', 'long', 'double', 'object', 'var', '>', ']']);

function classify(word: string, before: string, after: string): TokenKind {
  if (keywords.has(word) && before !== '.') return 'kw';
  if (members.has(word)) return 'prop';
  if (before === 'new' || (beforeType.has(before) && /^[A-Z]/.test(word) && after !== '=')) return 'type';
  if (after === '(') return 'fn';
  // A member, read (dialog.Page) or set in an initializer (new() { Name = ... }), or declared (string Title).
  if (before === '.' || after === '=' || beforeMember.has(before)) return 'prop';
  return /^[A-Z]/.test(word) ? 'type' : 'plain';
}

// A string from its opening quote: "...", @"..." or $"..." with the code in its holes tokenized as code.
function scanString(code: string, start: number, tokens: Token[]): number {
  let i = start;
  let verbatim = false;
  let interpolated = false;
  while (code[i] !== '"') {
    if (code[i] === '@') verbatim = true;
    if (code[i] === '$') interpolated = true;
    i++;
  }
  i++;
  let text = code.slice(start, i);
  const flush = () => {
    if (text) tokens.push({ kind: 'str', text });
    text = '';
  };
  while (i < code.length) {
    const c = code[i];
    if (!verbatim && c === '\\') {
      text += code.slice(i, i + 2);
      i += 2;
    } else if (c === '"' && verbatim && code[i + 1] === '"') {
      text += '""';
      i += 2;
    } else if (c === '"') {
      text += '"';
      i++;
      break;
    } else if (interpolated && (c === '{' || c === '}') && code[i + 1] === c) {
      text += c + c;
      i += 2;
    } else if (interpolated && c === '{') {
      flush();
      const end = closingBrace(code, i);
      tokens.push({ kind: 'punc', text: '{' });
      tokens.push(...scan(code.slice(i + 1, end)));
      if (end < code.length) tokens.push({ kind: 'punc', text: '}' });
      i = end + 1;
    } else if (c === '\n' && !verbatim) {
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

function nextSignificant(code: string, from: number): string {
  let i = from;
  while (i < code.length && (code[i] === ' ' || code[i] === '\t')) i++;
  // A generic method: Expect<T>( is rare in a flow; a generic type, Task<ILocator>, is common.
  return code[i] === '=' && code[i + 1] === '=' ? '==' : (code[i] ?? '');
}

const endOfLine = (code: string, from: number) => {
  const end = code.indexOf('\n', from);
  return end < 0 ? code.length : end;
};
const isDigit = (c: string) => c >= '0' && c <= '9';
const isWordStart = (c: string) => /[A-Za-z_]/.test(c);
const isWord = (c: string) => /\w/.test(c);
