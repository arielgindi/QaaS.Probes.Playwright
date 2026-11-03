// `doc` is the text of a /// comment, `doctag` its XML tags, `docref` what a tag refers to (cref="NewTicketConfig").
export type TokenKind = 'plain' | 'kw' | 'type' | 'fn' | 'prop' | 'str' | 'num' | 'com' | 'doc' | 'doctag' | 'docref' | 'punc' | 'key';

export interface Token {
  kind: TokenKind;
  text: string;
}

/** The tokens of a text, split where its lines end: a token across a line end becomes one per line. */
export function splitLines(tokens: Token[]): Token[][] {
  const lines: Token[][] = [[]];
  for (const token of tokens) {
    const parts = token.text.split('\n');
    parts.forEach((part, index) => {
      if (index > 0) lines.push([]);
      if (part) lines[lines.length - 1].push({ kind: token.kind, text: part });
    });
  }
  return lines;
}
