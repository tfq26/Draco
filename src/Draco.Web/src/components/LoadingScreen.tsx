import { Spinner } from './ui/spinner'

interface LoadingScreenProps {
  message?: string
}

export function LoadingScreen({ message = "Initializing Neural Link..." }: LoadingScreenProps) {
  return (
    <div className="fixed inset-0 bg-background flex flex-col items-center justify-center z-[9999] overflow-hidden">
      <div className="page-bg-gradient" />
      
      <div className="relative flex flex-col items-center gap-8">
        <Spinner className="text-primary opacity-80 size-36" />
        
        <div className="text-center">
          <div className="monochrome-gradient text-2xl font-black uppercase tracking-[0.25em] mb-3 opacity-60">
            Draco
          </div>
          <div className="text-sm text-foreground font-medium opacity-90 tracking-wide">
            {message}
          </div>
        </div>
      </div>
    </div>
  )
}
