import { LucideProps, Loader2Icon } from "lucide-react"

import { cn } from "@/lib/utils"

function Spinner({ className, size = 16, ...props }: LucideProps) {
  return (
    <Loader2Icon
      role="status"
      aria-label="Loading"
      size={size}
      className={cn("animate-spin", className)}
      {...props}
    />
  )
}

export { Spinner }
