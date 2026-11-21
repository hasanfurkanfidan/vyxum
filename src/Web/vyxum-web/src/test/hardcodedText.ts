// Finds UI text written directly in TSX instead of coming from `t(key)` or from data.
// It parses the source with the TypeScript compiler and reports:
//  - JSX text between tags that contains a letter (`<p>Save</p>`, `{n} kg`);
//  - string literals with a letter in text attributes (`title="Save"`, `aria-label={'Save'}`);
//  - string literals with a letter rendered as a child (`{'Save'}`, `{ok ? 'Yes' : t('a.No')}`).
// String literals passed to a function call (`t('shell.Save')`) and literals used in comparisons
// (`status === 'ready'`) are not rendered text and are allowed.
import ts from 'typescript';

export interface HardcodedText {
  file: string;
  line: number;
  text: string;
}

const textAttributes = new Set([
  'alt',
  'aria-description',
  'aria-label',
  'aria-placeholder',
  'aria-roledescription',
  'aria-valuetext',
  'label',
  'placeholder',
  'title',
]);

const letter = /\p{L}/u;

function hasLetter(text: string): boolean {
  // HTML entities such as &nbsp; are not words.
  return letter.test(text.replace(/&[a-zA-Z]+;|&#\d+;/g, ''));
}

/** String literals that end up rendered when `expression` is rendered. */
function renderedLiterals(expression: ts.Expression): ts.Node[] {
  if (ts.isStringLiteral(expression) || ts.isNoSubstitutionTemplateLiteral(expression) || ts.isTemplateExpression(expression)) {
    return [expression];
  }
  if (ts.isParenthesizedExpression(expression)) return renderedLiterals(expression.expression);
  if (ts.isConditionalExpression(expression)) {
    return [...renderedLiterals(expression.whenTrue), ...renderedLiterals(expression.whenFalse)];
  }
  if (ts.isBinaryExpression(expression)) {
    const op = expression.operatorToken.kind;
    if (op === ts.SyntaxKind.AmpersandAmpersandToken) return renderedLiterals(expression.right);
    if (op === ts.SyntaxKind.BarBarToken || op === ts.SyntaxKind.QuestionQuestionToken || op === ts.SyntaxKind.PlusToken) {
      return [...renderedLiterals(expression.left), ...renderedLiterals(expression.right)];
    }
  }
  return [];
}

function literalText(node: ts.Node): string {
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) return node.text;
  if (ts.isTemplateExpression(node)) return [node.head.text, ...node.templateSpans.map((s) => s.literal.text)].join(' ');
  return '';
}

export function findHardcodedText(source: string, file = 'source.tsx'): HardcodedText[] {
  const sourceFile = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const found: HardcodedText[] = [];

  const report = (node: ts.Node, text: string) => {
    const { line } = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile));
    found.push({ file, line: line + 1, text: text.trim() });
  };

  const visit = (node: ts.Node) => {
    if (ts.isJsxText(node) && hasLetter(node.text)) {
      report(node, node.text);
    } else if (ts.isJsxAttribute(node) && node.initializer) {
      const name = node.name.getText(sourceFile);
      if (textAttributes.has(name)) {
        const init = node.initializer;
        const literals = ts.isStringLiteral(init) ? [init] : ts.isJsxExpression(init) && init.expression ? renderedLiterals(init.expression) : [];
        for (const literal of literals) {
          const text = literalText(literal);
          if (hasLetter(text)) report(literal, text);
        }
      }
    } else if (ts.isJsxExpression(node) && node.expression && (ts.isJsxElement(node.parent) || ts.isJsxFragment(node.parent))) {
      for (const literal of renderedLiterals(node.expression)) {
        const text = literalText(literal);
        if (hasLetter(text)) report(literal, text);
      }
    }
    ts.forEachChild(node, visit);
  };

  visit(sourceFile);
  return found;
}
