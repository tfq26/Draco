import { useMemo, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Bot, ChevronRight, Loader2, MessageCircle, Sparkles, X } from 'lucide-react'
import { dracoApi, type AutonomousInsightQueryResponse } from '../lib/api'

type ChatMessage =
  | {
      id: string
      role: 'assistant'
      content: string
      report?: AutonomousInsightQueryResponse['report']
    }
  | {
      id: string
      role: 'user'
      content: string
    }

const starterPrompts = [
  'How is my storage looking right now?',
  'What should I pay attention to this week?',
  'Which resources look most expensive?',
]

export function AssistantWidget() {
  const [isOpen, setIsOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      id: 'welcome',
      role: 'assistant',
      content:
        "Ask Draco about your environment, costs, storage, or active risks. I'll summarize what I'm seeing and propose next steps, but I won't take action without your approval.",
    },
  ])

  const latestReport = useMemo(() => {
    for (const message of [...messages].reverse()) {
      if (message.role === 'assistant' && 'report' in message && message.report) {
        return message.report
      }
    }

    return undefined
  }, [messages])

  const queryMutation = useMutation({
    mutationFn: (nextQuery: string) => dracoApi.dashboard.queryInsights(nextQuery),
    onSuccess: (response, nextQuery) => {
      setMessages(prev => [
        ...prev,
        { id: `user-${crypto.randomUUID()}`, role: 'user', content: nextQuery },
        {
          id: `assistant-${crypto.randomUUID()}`,
          role: 'assistant',
          content: response.answer,
          report: response.report,
        },
      ])
      setQuery('')
    },
  })

  const handleSubmit = async (nextQuery?: string) => {
    const prompt = (nextQuery ?? query).trim()
    if (!prompt || queryMutation.isPending) {
      return
    }

    await queryMutation.mutateAsync(prompt)
  }

  return (
    <div className="assistant-shell">
      {isOpen && (
        <div className="assistant-panel premium-glass">
          <div className="assistant-header">
            <div>
              <div className="assistant-eyebrow">
                <Sparkles size={12} />
                Draco AI
              </div>
              <div className="assistant-title">Cloud Assistant</div>
              <div className="assistant-subtitle">Grounded answers across your connected environments</div>
            </div>
            <button
              type="button"
              className="assistant-icon-button"
              onClick={() => setIsOpen(false)}
              aria-label="Close assistant"
            >
              <X size={16} />
            </button>
          </div>

          <div className="assistant-body">
            <div className="assistant-messages">
              {messages.map(message => (
                <div
                  key={message.id}
                  className={`assistant-message assistant-message-${message.role}`}
                >
                  {message.role === 'assistant' && (
                    <div className="assistant-avatar">
                      <Bot size={14} />
                    </div>
                  )}
                  <div className="assistant-bubble">
                    <div style={{ whiteSpace: 'pre-wrap' }}>{message.content}</div>

                    {message.role === 'assistant' && message.report && (
                      <div className="assistant-report-card">
                        <div className="assistant-report-header">
                          <span>{message.report.focusArea} report</span>
                          <span>{message.report.resourcesInScope.length} resources in scope</span>
                        </div>

                        {message.report.findings.length > 0 && (
                          <div className="assistant-section">
                            {message.report.findings.slice(0, 3).map(finding => (
                              <div key={finding} className="assistant-line-item">{finding}</div>
                            ))}
                          </div>
                        )}

                        {message.report.proposedActions.length > 0 && (
                          <div className="assistant-section">
                            <div className="assistant-section-title">Needs your approval</div>
                            {message.report.proposedActions.slice(0, 2).map(action => (
                              <div key={`${action.resourceId}-${action.action}`} className="assistant-action-item">
                                <div>
                                  <div className="assistant-action-title">{action.label} {action.resourceName}</div>
                                  <div className="assistant-action-copy">{action.reason}</div>
                                </div>
                                <span className="assistant-pill">Review</span>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                </div>
              ))}

              {queryMutation.isPending && (
                <div className="assistant-message assistant-message-assistant">
                  <div className="assistant-avatar">
                    <Bot size={14} />
                  </div>
                  <div className="assistant-bubble assistant-loading">
                    <Loader2 size={14} className="animate-spin" />
                    Draco is reviewing your environment...
                  </div>
                </div>
              )}
            </div>

            {!latestReport && (
              <div className="assistant-starters">
                {starterPrompts.map(prompt => (
                  <button
                    key={prompt}
                    type="button"
                    className="assistant-starter"
                    onClick={() => void handleSubmit(prompt)}
                  >
                    <span>{prompt}</span>
                    <ChevronRight size={14} />
                  </button>
                ))}
              </div>
            )}

            {latestReport && (
              <div className="assistant-summary-bar">
                <div>
                  <div className="assistant-summary-label">Latest scope</div>
                  <div className="assistant-summary-value">
                    {latestReport.focusArea} across {latestReport.resourcesInScope.length} resources
                  </div>
                </div>
                <div>
                  <div className="assistant-summary-label">Policy</div>
                  <div className="assistant-summary-value">Approval required for all actions</div>
                </div>
              </div>
            )}
          </div>

          <form
            className="assistant-composer"
            onSubmit={event => {
              event.preventDefault()
              void handleSubmit()
            }}
          >
            <textarea
              value={query}
              onChange={event => setQuery(event.target.value)}
              placeholder="Ask about storage, costs, anomalies, or a specific resource..."
              rows={1}
              className="assistant-input"
            />
            <button
              type="submit"
              className="btn-primary assistant-send"
              disabled={queryMutation.isPending || !query.trim()}
            >
              Ask Draco
            </button>
          </form>
        </div>
      )}

      <button
        type="button"
        className={`assistant-launcher ${isOpen ? 'assistant-launcher-open' : ''}`}
        onClick={() => setIsOpen(open => !open)}
        aria-label={isOpen ? 'Close Draco assistant' : 'Open Draco assistant'}
      >
        {isOpen ? <X size={18} /> : <MessageCircle size={18} />}
        <span>{isOpen ? 'Close' : 'Ask Draco'}</span>
      </button>
    </div>
  )
}
