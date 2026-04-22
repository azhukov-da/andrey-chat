import { lazy, Suspense } from 'react'
import { createBrowserRouter, Navigate } from 'react-router'
import AppShell from '@/features/layout/AppShell'

const SignIn = lazy(() => import('@/features/auth/SignIn'))
const Register = lazy(() => import('@/features/auth/Register'))
const ForgotPassword = lazy(() => import('@/features/auth/ForgotPassword'))
const ResetPassword = lazy(() => import('@/features/auth/ResetPassword'))
const PublicCatalog = lazy(() => import('@/features/rooms/PublicCatalog'))
const ChatWindow = lazy(() => import('@/features/chat/ChatWindow'))
const FriendList = lazy(() => import('@/features/friends/FriendList'))
const ProfilePage = lazy(() => import('@/features/profile/ProfilePage'))
const SessionsPage = lazy(() => import('@/features/sessions/SessionsPage'))

const Loading = () => (
  <div className="flex h-full items-center justify-center">
    <span className="loading loading-spinner loading-lg" />
  </div>
)

const wrap = (el: React.ReactElement) => <Suspense fallback={<Loading />}>{el}</Suspense>

export const router = createBrowserRouter([
  { path: '/login', element: wrap(<SignIn />) },
  { path: '/register', element: wrap(<Register />) },
  { path: '/forgot', element: wrap(<ForgotPassword />) },
  { path: '/reset', element: wrap(<ResetPassword />) },
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <Navigate to="/rooms" replace /> },
      { path: 'rooms', element: wrap(<PublicCatalog />) },
      { path: 'rooms/:id', element: wrap(<ChatWindow />) },
      { path: 'contacts', element: wrap(<FriendList />) },
      { path: 'profile', element: wrap(<ProfilePage />) },
      { path: 'sessions', element: wrap(<SessionsPage />) },
    ],
  },
])
