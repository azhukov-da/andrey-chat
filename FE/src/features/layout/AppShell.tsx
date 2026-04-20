import { useEffect } from 'react'
import { Outlet, useNavigate } from 'react-router'
import { useMe } from '@/hooks/useAuth'
import { useSignalR } from '@/hooks/useSignalR'
import TopNav from './TopNav'
import RightSidebar from './RightSidebar'

export default function AppShell() {
  const { data: me, isError, isLoading } = useMe()
  const navigate = useNavigate()
  useSignalR()

  useEffect(() => {
    if (!isLoading && isError) {
      navigate('/login')
    }
  }, [isLoading, isError, navigate])

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <span className="loading loading-spinner loading-lg" />
      </div>
    )
  }

  if (!me) return null

  return (
    <div className="flex flex-col h-screen">
      <TopNav me={me} />
      <div className="flex flex-1 overflow-hidden">
        <main className="flex-1 overflow-hidden">
          <Outlet />
        </main>
        <RightSidebar />
      </div>
    </div>
  )
}
