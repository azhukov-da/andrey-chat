import { NavLink } from 'react-router'
import { useAuth } from '@/hooks/useAuth'
import { useUnread } from '@/hooks/useUnread'
import type { UserProfile } from '@/types'

interface Props {
  me: UserProfile
}

export default function TopNav({ me }: Props) {
  const { logout } = useAuth()
  const { total } = useUnread()

  return (
    <div className="navbar bg-base-100 border-b border-base-300 z-10">
      <div className="navbar-start">
        <span className="text-xl font-bold px-4">💬 Chat</span>
      </div>
      <div className="navbar-center">
        <ul className="menu menu-horizontal px-1 gap-1">
          <li>
            <NavLink to="/rooms" end className={({ isActive }) => isActive ? 'active' : ''}>
              Public Rooms
              {total > 0 && <span className="badge badge-primary badge-sm">{total}</span>}
            </NavLink>
          </li>
          <li><NavLink to="/rooms/private" className={({ isActive }) => isActive ? 'active' : ''}>Private Rooms</NavLink></li>
          <li><NavLink to="/contacts" className={({ isActive }) => isActive ? 'active' : ''}>Contacts</NavLink></li>
          <li><NavLink to="/profile" className={({ isActive }) => isActive ? 'active' : ''}>Profile</NavLink></li>
          <li><NavLink to="/sessions" className={({ isActive }) => isActive ? 'active' : ''}>Sessions</NavLink></li>
        </ul>
      </div>
      <div className="navbar-end pr-4">
        <div className="dropdown dropdown-end">
          <label tabIndex={0} className="btn btn-ghost btn-sm">
            {me.displayName ?? me.userName}
            <svg xmlns="http://www.w3.org/2000/svg" className="h-4 w-4 ml-1" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
            </svg>
          </label>
          <ul tabIndex={0} className="dropdown-content menu bg-base-100 rounded-box shadow z-50 w-40 p-1">
            <li><NavLink to="/profile">Profile</NavLink></li>
            <li><NavLink to="/sessions">Sessions</NavLink></li>
            <li><button onClick={logout} className="text-error">Sign Out</button></li>
          </ul>
        </div>
      </div>
    </div>
  )
}
