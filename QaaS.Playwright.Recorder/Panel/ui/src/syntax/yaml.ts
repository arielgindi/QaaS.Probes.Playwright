import type { Token } from './token';

// The YAML the recorder writes: comments, keys, and scalars that are quoted text, flags, numbers or {}.

const scalar = /^('(?:[^']|'')*'|"(?:[^"\\]|\\.)*"|true|false|null|~|-?\d+(?:\.\d+)?|\{\}|\[\])(\s*#.*)?$/;

/** The YAML's tokens, line by line. */
export function tokenizeYaml(text: string): Token[][] {
  return text.split('\n').map(tokenizeLine);
}

function tokenizeLine(line: string): Token[] {
  const comment = /^(\s*)(#.*)$/.exec(line);
  if (comment) return compact([{ kind: 'plain', text: comment[1] }, { kind: 'com', text: comment[2] }]);
  const entry = /^(\s*)(- )?([^:#'"\s][^:#]*?)(:)(\s*)(.*)$/.exec(line);
  if (!entry) return compact([{ kind: 'plain', text: line }]);
  const [, indent, dash, key, colon, space, value] = entry;
  return compact([
    { kind: 'plain', text: indent },
    { kind: 'punc', text: dash ?? '' },
    { kind: 'key', text: key },
    { kind: 'punc', text: colon },
    { kind: 'plain', text: space },
    ...tokenizeValue(value),
  ]);
}

function tokenizeValue(value: string): Token[] {
  const match = scalar.exec(value);
  if (!match) return [{ kind: 'str', text: value }];
  const [, text, comment] = match;
  const kind = /^['"]/.test(text) ? 'str' : /^(true|false|null|~)$/.test(text) ? 'kw' : /^[{[]/.test(text) ? 'punc' : 'num';
  return [{ kind, text }, { kind: 'com', text: comment ?? '' }];
}

const compact = (tokens: Token[]) => tokens.filter((token) => token.text);
