import React, { useMemo, useState, useEffect, useRef } from 'react'

/** 
 * D-CHART ENGINE (DRACO)
 * A custom, high-performant SVG-based graphing engine.
 * Optimized for real-time cloud data visualization.
 */

export interface DataPoint {
  x: number | string
  y: number
  metadata?: any
}

function useDimensions<T extends HTMLElement>() {
  const ref = useRef<T>(null)
  const [dimensions, setDimensions] = useState({ width: 0, height: 0 })

  useEffect(() => {
    if (!ref.current) return
    const observer = new ResizeObserver((entries) => {
      if (entries[0]) {
        setDimensions({
          width: entries[0].contentRect.width,
          height: entries[0].contentRect.height
        })
      }
    })
    observer.observe(ref.current)
    return () => observer.disconnect()
  }, [])

  return [ref, dimensions] as const
}

interface ChartBaseProps {
  data: DataPoint[]
  width?: number | string
  height?: number | string
  color?: string
  gradient?: boolean
  strokeWidth?: number
  showGrid?: boolean
  unit?: string
  label?: string
  animate?: boolean
}

export const LineChart: React.FC<ChartBaseProps> = ({
  data,
  height = 300,
  color = 'var(--primary)',
  gradient = false,
  strokeWidth = 2,
  showGrid = true,
  unit = '',
  label = ''
}) => {
  const [containerRef, dimensions] = useDimensions<HTMLDivElement>()
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null)

  const { points, gridLines, width, svgHeight, padding, svgId } = useMemo(() => {
    const numericX = data.map((d, i) => (typeof d.x === 'number' ? d.x : i))
    const minX = Math.min(...numericX)
    const maxX = Math.max(...numericX)
    const minY = 0
    const maxY = Math.max(...data.map(d => d.y)) * 1.1

    const w = dimensions.width || 1000
    const h = dimensions.height || 400
    const p = { top: 20, right: 30, bottom: 40, left: 60 }

    const pts = data.map((d, i) => {
      const xVal = typeof d.x === 'number' ? d.x : i
      const x = p.left + ((xVal - minX) / (maxX - minX)) * (w - p.left - p.right)
      const y = h - p.bottom - ((d.y - minY) / (maxY - minY)) * (h - p.top - p.bottom)
      return { x, y, raw: d }
    })

    const gridLines = Array.from({ length: 5 }).map((_, i) => {
      const val = minY + (maxY - minY) * (i / 4)
      const yLabel = val.toFixed(0)
      const y = h - p.bottom - (i / 4) * (h - p.top - p.bottom)
      return { y, label: yLabel }
    })

    const svgId = label.replace(/\s+/g, '-').toLowerCase()
    return { points: pts, gridLines, width: w, svgHeight: h, padding: p, svgId }
  }, [data, label, dimensions])

  const pathData = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x},${p.y}`).join(' ')
  const areaData = points.length > 0 ? `${pathData} L${points[points.length - 1].x},${svgHeight - padding.bottom} L${points[0].x},${svgHeight - padding.bottom} Z` : ''

  return (
    <div ref={containerRef} style={{ width: '100%', height, position: 'relative', fontFamily: "'Roboto', sans-serif" }}>
      <svg
        viewBox={`0 0 ${width} ${svgHeight}`}
        style={{ width: '100%', height: '100%', overflow: 'visible' }}
      >
        <defs>
          <linearGradient id={`chartGradient-${svgId}`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={color} stopOpacity="0.5" />
            <stop offset="100%" stopColor={color} stopOpacity="0.05" />
          </linearGradient>
        </defs>

        {showGrid && gridLines.map((line, i) => (
          <React.Fragment key={i}>
            <line x1={padding.left} y1={line.y} x2={width - padding.right} y2={line.y} stroke="var(--border)" strokeWidth="1" strokeDasharray="4 4" style={{ opacity: 0.5 }} />
            <text x={padding.left - 10} y={line.y + 4} fill="var(--muted-foreground)" textAnchor="end" fontSize="12" style={{ fontWeight: 500 }}>{line.label}{unit}</text>
          </React.Fragment>
        ))}

        {gradient && (
          <path d={areaData} fill={`url(#chartGradient-${svgId})`} style={{ transition: 'all 0.3s' }} />
        )}

        <path
          d={pathData}
          fill="none"
          stroke={color}
          strokeWidth={strokeWidth}
          strokeLinecap="round"
          strokeLinejoin="round"
          style={{ transition: 'all 0.3s' }}
        />

        {points.map((p, i) => (
          <g key={i} onMouseEnter={() => setHoveredIndex(i)} onMouseLeave={() => setHoveredIndex(null)}>
            <circle
              cx={p.x}
              cy={p.y}
              r={hoveredIndex === i ? 6 : 0}
              fill={color}
              stroke="#ffffff"
              strokeWidth="2"
              style={{ transition: 'r 0.2s' }}
            />
            <rect x={p.x - 10} y={0} width="20" height={svgHeight} fill="transparent" style={{ cursor: 'pointer' }} />
          </g>
        ))}
      </svg>

      {hoveredIndex !== null && (
        <div style={{
          position: 'absolute',
          left: `${(points[hoveredIndex].x / width) * 100}%`,
          top: `${(points[hoveredIndex].y / svgHeight) * 100}%`,
          transform: 'translate(-50%, -120%)',
          background: 'var(--card)',
          border: '1px solid var(--border)',
          padding: '0.5rem 0.75rem',
          borderRadius: 'var(--radius-md)',
          boxShadow: '0 4px 12px rgba(0,0,0,0.1)',
          pointerEvents: 'none',
          zIndex: 10,
          whiteSpace: 'nowrap'
        }}>
          <div style={{ fontSize: '0.65rem', color: 'var(--muted-foreground)', textTransform: 'uppercase' }}>{points[hoveredIndex].raw.x}</div>
          <div style={{ fontSize: '0.875rem', fontWeight: 800 }}>{(Math.ceil(points[hoveredIndex].raw.y * 1000) / 1000).toFixed(3)}{unit}</div>
        </div>
      )}

      {label && (
        <div 
          title={label}
          style={{ 
            position: 'absolute', 
            top: 0, 
            right: padding.right + 20, 
            fontSize: '0.65rem', 
            fontWeight: 700, 
            color: 'var(--muted-foreground)', 
            textTransform: 'uppercase', 
            letterSpacing: '0.05em'
          }}
        >
          {label}
        </div>
      )}

      <style>{`
        @keyframes drawPath {
          to { strokeDashoffset: 0; }
        }
      `}</style>
    </div>
  )
}

export const BarChart: React.FC<ChartBaseProps> = ({
  data,
  height = 200,
  color = 'var(--primary)',
  label = ''
}) => {
  const [containerRef, dimensions] = useDimensions<HTMLDivElement>()
  const [hoverIdx, setHoverIdx] = useState<number | null>(null)
  const { bars, width, svgHeight, padding } = useMemo(() => {
    const totalY = data.reduce((sum, d) => sum + d.y, 0)
    const maxY = Math.max(...data.map(d => d.y)) * 1.1
    const w = dimensions.width || 1000
    const h = dimensions.height || 400
    const p = { top: 20, right: 20, bottom: 40, left: 60 }
    
    const availableWidth = w - p.left - p.right
    const barWidth = (availableWidth / data.length) * 0.7

    const bs = data.map((d, i) => {
      const x = p.left + (i / data.length) * availableWidth + (barWidth * 0.15)
      const barH = ((d.y) / maxY) * (h - p.top - p.bottom)
      const y = h - p.bottom - barH
      const percentage = totalY > 0 ? (d.y / totalY) * 100 : 0
      return { x, y, w: barWidth, h: barH, raw: d, percentage }
    })

    return { bars: bs, width: w, svgHeight: h, padding: p }
  }, [data, dimensions])

  return (
    <div ref={containerRef} style={{ width: '100%', height, position: 'relative', fontFamily: "'Roboto', sans-serif" }}>
      <svg viewBox={`0 0 ${width} ${svgHeight}`} style={{ width: '100%', height: '100%', overflow: 'visible' }}>
        {bars.map((bar, i) => (
          <g key={i} onMouseEnter={() => setHoverIdx(i)} onMouseLeave={() => setHoverIdx(null)}>
            <rect
              x={bar.x}
              y={bar.y}
              width={bar.w}
              height={bar.h}
              fill={color}
              rx={4}
              style={{ 
                transformOrigin: `${bar.x + bar.w / 2}px ${svgHeight - padding.bottom}px`, 
                animation: `growBar 1s ${i * 0.05}s forwards cubic-bezier(0.16, 1, 0.3, 1)`, 
                transform: 'scaleY(0)',
                cursor: 'pointer',
                opacity: hoverIdx === null || hoverIdx === i ? 1 : 0.6,
                transition: 'opacity 0.2s'
              }}
            />
            {bar.h > 30 && (
              <text
                x={bar.x + bar.w / 2}
                y={bar.y + bar.h / 2 + 5}
                textAnchor="middle"
                fill="white"
                fontSize="14"
                fontWeight="900"
                style={{ pointerEvents: 'none', opacity: 0, animation: `fadeIn 0.5s ${1 + i * 0.05}s forwards` }}
              >
                {bar.percentage.toFixed(1)}%
              </text>
            )}
            {hoverIdx === i && (
              <text
                x={bar.x + bar.w / 2}
                y={bar.y - 8}
                textAnchor="middle"
                fill="var(--foreground)"
                fontSize="12"
                fontWeight="700"
              >
                {(Math.ceil(bar.raw.y * 1000) / 1000).toFixed(3)}
              </text>
            )}
            <text x={bar.x + bar.w / 2} y={svgHeight - 15} textAnchor="middle" fill="var(--muted-foreground)" fontSize="10" style={{ fontWeight: 500 }}>{bar.raw.x}</text>
          </g>
        ))}
      </svg>
      {label && (
        <div 
          title={label}
          style={{ 
            position: 'absolute', 
            top: 0, 
            right: 0, 
            fontSize: '0.65rem', 
            fontWeight: 700, 
            color: 'var(--muted-foreground)', 
            textTransform: 'uppercase', 
            letterSpacing: '0.05em'
          }}
        >
          {label}
        </div>
      )}
      <style>{`
        @keyframes growBar { to { transform: scaleY(1); } }
        @keyframes fadeIn { to { opacity: 1; } }
      `}</style>
    </div>
  )
}
