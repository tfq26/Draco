import { Fragment, type ReactNode } from 'react'

type MarkdownBlock =
  | { type: 'heading'; level: 1 | 2 | 3; text: string }
  | { type: 'paragraph'; text: string }
  | { type: 'unordered-list'; items: string[] }
  | { type: 'ordered-list'; items: string[] }
  | { type: 'code'; language?: string; code: string }

function renderInline(text: string): ReactNode[] {
  const nodes: ReactNode[] = []
  const pattern = /(\[([^\]]+)\]\(([^)]+)\)|`([^`]+)`|\*\*([^*]+)\*\*)/g
  let cursor = 0
  let match: RegExpExecArray | null

  while ((match = pattern.exec(text)) !== null) {
    if (match.index > cursor) {
      nodes.push(text.slice(cursor, match.index))
    }

    if (match[2] && match[3]) {
      nodes.push(
        <a
          key={`${match.index}-link`}
          href={match[3]}
          target="_blank"
          rel="noreferrer"
          style={{ color: 'var(--primary)', textDecoration: 'underline', fontWeight: 600 }}
        >
          {match[2]}
        </a>,
      )
    } else if (match[4]) {
      nodes.push(
        <code
          key={`${match.index}-code`}
          style={{
            fontFamily: 'var(--font-mono)',
            background: 'rgba(255,255,255,0.06)',
            padding: '0.1rem 0.35rem',
            borderRadius: '0.35rem',
          }}
        >
          {match[4]}
        </code>,
      )
    } else if (match[5]) {
      nodes.push(<strong key={`${match.index}-strong`}>{match[5]}</strong>)
    }

    cursor = pattern.lastIndex
  }

  if (cursor < text.length) {
    nodes.push(text.slice(cursor))
  }

  return nodes
}

function parseMarkdown(markdown: string): MarkdownBlock[] {
  const normalized = markdown.replace(/\r\n/g, '\n').trim()
  const lines = normalized.split('\n')
  const blocks: MarkdownBlock[] = []
  let index = 0

  while (index < lines.length) {
    const line = lines[index].trimEnd()

    if (!line.trim()) {
      index += 1
      continue
    }

    if (line.startsWith('```')) {
      const language = line.slice(3).trim() || undefined
      index += 1
      const codeLines: string[] = []
      while (index < lines.length && !lines[index].startsWith('```')) {
        codeLines.push(lines[index])
        index += 1
      }
      index += 1
      blocks.push({ type: 'code', language, code: codeLines.join('\n') })
      continue
    }

    if (line.startsWith('### ')) {
      blocks.push({ type: 'heading', level: 3, text: line.slice(4).trim() })
      index += 1
      continue
    }

    if (line.startsWith('## ')) {
      blocks.push({ type: 'heading', level: 2, text: line.slice(3).trim() })
      index += 1
      continue
    }

    if (line.startsWith('# ')) {
      blocks.push({ type: 'heading', level: 1, text: line.slice(2).trim() })
      index += 1
      continue
    }

    if (line.startsWith('- ')) {
      const items: string[] = []
      while (index < lines.length && lines[index].trimStart().startsWith('- ')) {
        items.push(lines[index].trimStart().slice(2).trim())
        index += 1
      }
      blocks.push({ type: 'unordered-list', items })
      continue
    }

    if (/^\d+\.\s/.test(line)) {
      const items: string[] = []
      while (index < lines.length && /^\d+\.\s/.test(lines[index].trimStart())) {
        items.push(lines[index].trimStart().replace(/^\d+\.\s/, '').trim())
        index += 1
      }
      blocks.push({ type: 'ordered-list', items })
      continue
    }

    const paragraphLines: string[] = [line.trim()]
    index += 1
    while (
      index < lines.length &&
      lines[index].trim() &&
      !lines[index].startsWith('#') &&
      !lines[index].trimStart().startsWith('- ') &&
      !/^\d+\.\s/.test(lines[index].trimStart()) &&
      !lines[index].startsWith('```')
    ) {
      paragraphLines.push(lines[index].trim())
      index += 1
    }

    blocks.push({ type: 'paragraph', text: paragraphLines.join(' ') })
  }

  return blocks
}

export function MarkdownPage({ markdown }: { markdown: string }) {
  const blocks = parseMarkdown(markdown)

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
      {blocks.map((block, index) => {
        if (block.type === 'heading') {
          const fontSize = block.level === 1 ? '2.25rem' : block.level === 2 ? '1.35rem' : '1rem'
          const marginTop = block.level === 1 ? '0' : '1rem'
          return (
            <div key={index} style={{ marginTop }}>
              {block.level === 1 ? (
                <h1 style={{ fontSize, fontWeight: 900, letterSpacing: '-0.04em', margin: 0 }}>{block.text}</h1>
              ) : block.level === 2 ? (
                <h2 style={{ fontSize, fontWeight: 800, margin: 0 }}>{block.text}</h2>
              ) : (
                <h3 style={{ fontSize, fontWeight: 700, margin: 0, color: 'var(--foreground)' }}>{block.text}</h3>
              )}
            </div>
          )
        }

        if (block.type === 'paragraph') {
          return (
            <p key={index} style={{ margin: 0, color: 'var(--muted-foreground)', lineHeight: 1.7 }}>
              {renderInline(block.text)}
            </p>
          )
        }

        if (block.type === 'unordered-list') {
          return (
            <ul key={index} style={{ margin: 0, paddingLeft: '1.25rem', display: 'flex', flexDirection: 'column', gap: '0.55rem' }}>
              {block.items.map((item, itemIndex) => (
                <li key={itemIndex} style={{ color: 'var(--foreground)', lineHeight: 1.6 }}>
                  {renderInline(item)}
                </li>
              ))}
            </ul>
          )
        }

        if (block.type === 'ordered-list') {
          return (
            <ol key={index} style={{ margin: 0, paddingLeft: '1.25rem', display: 'flex', flexDirection: 'column', gap: '0.55rem' }}>
              {block.items.map((item, itemIndex) => (
                <li key={itemIndex} style={{ color: 'var(--foreground)', lineHeight: 1.6 }}>
                  {renderInline(item)}
                </li>
              ))}
            </ol>
          )
        }

        return (
          <pre
            key={index}
            style={{
              margin: 0,
              padding: '1rem 1.25rem',
              borderRadius: 'var(--radius-lg)',
              background: 'rgba(255,255,255,0.03)',
              border: '1px solid var(--border)',
              overflowX: 'auto',
              fontSize: '0.85rem',
              lineHeight: 1.6,
            }}
          >
            <code>{block.code}</code>
          </pre>
        )
      })}
    </div>
  )
}

export function MarkdownSection({ markdown }: { markdown: string }) {
  return <Fragment>{<MarkdownPage markdown={markdown} />}</Fragment>
}
