import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import {
  banMember,
  deleteRoom,
  inviteUserToRoom,
  listBanned,
  listMembers,
  makeAdmin,
  removeAdmin,
  removeMember,
  unbanMember,
  updateRoom,
} from '@/api/rooms'
import { RoomRole, type Room, type RoomMember, type RoomVisibilityValue } from '@/types'
import PresenceDot from '@/features/chat/PresenceDot'
import { usePresence } from '@/hooks/usePresence'

interface Props {
  room: Room
  onClose: () => void
}

type TabKey = 'members' | 'admins' | 'banned' | 'invitations' | 'settings'

export default function ManageRoomDialog({ room, onClose }: Props) {
  const [tab, setTab] = useState<TabKey>('members')
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  const isOwner = room.myRole === RoomRole.Owner
  const isAdmin = room.myRole === RoomRole.Admin || isOwner

  const membersQuery = useQuery({
    queryKey: ['rooms', room.id, 'members'],
    queryFn: () => listMembers(room.id),
    enabled: isAdmin,
  })

  const bannedQuery = useQuery({
    queryKey: ['rooms', room.id, 'bans'],
    queryFn: () => listBanned(room.id),
    enabled: isAdmin && tab === 'banned',
  })

  const refreshMembers = () => {
    void queryClient.invalidateQueries({ queryKey: ['rooms', room.id, 'members'] })
    void queryClient.invalidateQueries({ queryKey: ['rooms', room.id, 'bans'] })
    void queryClient.invalidateQueries({ queryKey: ['rooms', room.id] })
  }

  const makeAdminMut = useMutation({
    mutationFn: (userId: string) => makeAdmin(room.id, userId),
    onSuccess: refreshMembers,
  })
  const removeAdminMut = useMutation({
    mutationFn: (userId: string) => removeAdmin(room.id, userId),
    onSuccess: refreshMembers,
  })
  const removeMemberMut = useMutation({
    mutationFn: (userId: string) => removeMember(room.id, userId),
    onSuccess: refreshMembers,
  })
  const banMut = useMutation({
    mutationFn: ({ userId, reason }: { userId: string; reason?: string | undefined }) =>
      banMember(room.id, userId, reason),
    onSuccess: refreshMembers,
  })
  const unbanMut = useMutation({
    mutationFn: (userId: string) => unbanMember(room.id, userId),
    onSuccess: refreshMembers,
  })

  const [settingsName, setSettingsName] = useState(room.name)
  const [settingsDescription, setSettingsDescription] = useState(room.description ?? '')
  const [settingsVisibility, setSettingsVisibility] = useState<RoomVisibilityValue>(
    room.visibility as RoomVisibilityValue,
  )
  const [settingsError, setSettingsError] = useState<string | null>(null)
  const [settingsSaved, setSettingsSaved] = useState(false)

  useEffect(() => {
    setSettingsName(room.name)
    setSettingsDescription(room.description ?? '')
    setSettingsVisibility(room.visibility as RoomVisibilityValue)
  }, [room.id, room.name, room.description, room.visibility])

  const updateMut = useMutation({
    mutationFn: () =>
      updateRoom(
        room.id,
        settingsName.trim(),
        settingsDescription.trim() ? settingsDescription.trim() : undefined,
        settingsVisibility,
      ),
    onSuccess: () => {
      setSettingsError(null)
      setSettingsSaved(true)
      void queryClient.invalidateQueries({ queryKey: ['rooms', room.id] })
      void queryClient.invalidateQueries({ queryKey: ['rooms', 'mine'] })
      void queryClient.invalidateQueries({ queryKey: ['rooms', 'public'] })
    },
    onError: (err: unknown) => {
      setSettingsSaved(false)
      const msg = err instanceof Error ? err.message : 'Failed to save changes.'
      setSettingsError(msg)
    },
  })

  const handleSaveSettings = (e: React.FormEvent) => {
    e.preventDefault()
    if (!isOwner) return
    if (!settingsName.trim()) {
      setSettingsError('Room name is required.')
      return
    }
    setSettingsError(null)
    setSettingsSaved(false)
    updateMut.mutate()
  }

  const [inviteUsername, setInviteUsername] = useState('')
  const [inviteError, setInviteError] = useState<string | null>(null)
  const [inviteSuccess, setInviteSuccess] = useState<string | null>(null)
  const inviteMut = useMutation({
    mutationFn: (username: string) => inviteUserToRoom(room.id, username),
    onSuccess: (_data, username) => {
      setInviteError(null)
      setInviteSuccess(`Invitation sent to @${username}.`)
      setInviteUsername('')
    },
    onError: (err: unknown) => {
      setInviteSuccess(null)
      const msg = err instanceof Error ? err.message : 'Failed to send invitation.'
      setInviteError(msg)
    },
  })

  const handleSendInvite = (e: React.FormEvent) => {
    e.preventDefault()
    const username = inviteUsername.trim()
    if (!username) {
      setInviteError('Username is required.')
      setInviteSuccess(null)
      return
    }
    setInviteError(null)
    setInviteSuccess(null)
    inviteMut.mutate(username)
  }

  const deleteMut = useMutation({
    mutationFn: () => deleteRoom(room.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['rooms', 'mine'] })
      void queryClient.invalidateQueries({ queryKey: ['rooms', 'public'] })
      onClose()
      navigate('/rooms')
    },
  })

  const members: RoomMember[] = membersQuery.data ?? []
  const admins = members.filter((m) => m.role === RoomRole.Admin || m.role === RoomRole.Owner)

  const [memberSearch, setMemberSearch] = useState('')
  const filteredMembers = members.filter((m) => {
    const q = memberSearch.trim().toLowerCase()
    if (!q) return true
    return (
      (m.userName ?? '').toLowerCase().includes(q) ||
      (m.displayName ?? '').toLowerCase().includes(q)
    )
  })

  const handleBan = (userId: string) => {
    const reason = window.prompt('Optional reason for ban:')
    if (reason && reason.trim().length > 0) {
      banMut.mutate({ userId, reason: reason.trim() })
    } else {
      banMut.mutate({ userId })
    }
  }

  const handleDelete = () => {
    if (!isOwner) return
    const ok = window.confirm(`Delete room "${room.name}"? This cannot be undone.`)
    if (!ok) return
    deleteMut.mutate()
  }

  return (
    <dialog open className="modal modal-open" data-testid="manage-room-dialog">
      <div className="modal-box max-w-2xl">
        <div className="flex items-center justify-between mb-3">
          <h3 className="font-bold text-lg">Manage room - {room.name}</h3>
          <button className="btn btn-sm btn-ghost" onClick={onClose}>Close</button>
        </div>

        <div role="tablist" className="tabs tabs-bordered mb-4">
          <button
            role="tab"
            className={`tab ${tab === 'members' ? 'tab-active' : ''}`}
            onClick={() => setTab('members')}
            data-testid="tab-members"
          >Members</button>
          <button
            role="tab"
            className={`tab ${tab === 'admins' ? 'tab-active' : ''}`}
            onClick={() => setTab('admins')}
            data-testid="tab-admins"
          >Admins</button>
          <button
            role="tab"
            className={`tab ${tab === 'banned' ? 'tab-active' : ''}`}
            onClick={() => setTab('banned')}
            data-testid="tab-banned"
          >Banned users</button>
          <button
            role="tab"
            className={`tab ${tab === 'invitations' ? 'tab-active' : ''}`}
            onClick={() => setTab('invitations')}
            data-testid="tab-invitations"
          >Invitations</button>
          <button
            role="tab"
            className={`tab ${tab === 'settings' ? 'tab-active' : ''}`}
            onClick={() => setTab('settings')}
            data-testid="tab-settings"
          >Settings</button>
        </div>

        {tab === 'members' && (
          <div data-testid="members-panel">
            {membersQuery.isLoading && <div className="loading loading-spinner" />}
            {membersQuery.isError && <div className="alert alert-error text-sm">Failed to load members.</div>}
            <div className="mb-3">
              <input
                type="search"
                data-testid="members-search"
                className="input input-bordered input-sm w-full"
                placeholder="Search member"
                value={memberSearch}
                onChange={(e) => setMemberSearch(e.target.value)}
                aria-label="Search member"
              />
            </div>
            <div className="overflow-x-auto">
              <table className="table table-sm" data-testid="members-table">
                <thead>
                  <tr>
                    <th data-testid="members-col-username">Username</th>
                    <th data-testid="members-col-status">Status</th>
                    <th data-testid="members-col-role">Role</th>
                    <th data-testid="members-col-actions">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredMembers.length === 0 && !membersQuery.isLoading && (
                    <tr data-testid="members-empty">
                      <td colSpan={4} className="py-2 text-sm text-base-content/60">
                        {memberSearch.trim() ? 'No members match your search.' : 'No members.'}
                      </td>
                    </tr>
                  )}
                  {filteredMembers.map((m) => (
                    <MemberRow
                      key={m.userId}
                      m={m}
                      isOwner={isOwner}
                      isAdmin={isAdmin}
                      onMakeAdmin={() => makeAdminMut.mutate(m.userId)}
                      onRemoveAdmin={() => removeAdminMut.mutate(m.userId)}
                      onBan={() => handleBan(m.userId)}
                      onRemove={() => removeMemberMut.mutate(m.userId)}
                    />
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {tab === 'admins' && (
          <div data-testid="admins-panel">
            <ul className="divide-y divide-base-300">
              {admins.map((m) => (
                <li key={m.userId} className="flex items-center justify-between py-2">
                  <div>
                    <div className="font-medium">{m.displayName || m.userName}</div>
                    <div className="text-xs text-base-content/60">
                      @{m.userName} - {m.role === RoomRole.Owner ? 'Owner' : 'Admin'}
                    </div>
                  </div>
                  {isOwner && m.role === RoomRole.Admin && (
                    <button className="btn btn-xs" onClick={() => removeAdminMut.mutate(m.userId)}>Remove admin</button>
                  )}
                </li>
              ))}
              {admins.length === 0 && <li className="py-2 text-sm text-base-content/60">No admins yet.</li>}
            </ul>
          </div>
        )}

        {tab === 'banned' && (
          <div data-testid="banned-panel">
            {bannedQuery.isLoading && <div className="loading loading-spinner" />}
            {bannedQuery.isError && <div className="alert alert-error text-sm">Failed to load banned users.</div>}
            {(bannedQuery.data ?? []).length > 0 && (
              <div className="overflow-x-auto">
                <table className="table table-sm" data-testid="banned-table">
                  <thead>
                    <tr>
                      <th>Username</th>
                      <th data-testid="banned-col-banned-by">Banned by</th>
                      <th data-testid="banned-col-date-time">Date/time</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(bannedQuery.data ?? []).map((b) => (
                      <tr key={b.bannedUserId} data-testid={`banned-row-${b.bannedUserId}`}>
                        <td>
                          <div className="font-medium">@{b.bannedUserName}</div>
                          {b.reason && (
                            <div className="text-xs text-base-content/60">Reason: {b.reason}</div>
                          )}
                        </td>
                        <td data-testid={`banned-by-${b.bannedUserId}`}>
                          {b.bannedByUserName ? `@${b.bannedByUserName}` : '-'}
                        </td>
                        <td data-testid={`banned-at-${b.bannedUserId}`}>
                          {b.createdAt ? new Date(b.createdAt).toLocaleString() : '-'}
                        </td>
                        <td>
                          {isAdmin && (
                            <button className="btn btn-xs" onClick={() => unbanMut.mutate(b.bannedUserId)}>Unban</button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            {(bannedQuery.data ?? []).length === 0 && !bannedQuery.isLoading && (
              <div className="py-2 text-sm text-base-content/60">No banned users.</div>
            )}
          </div>
        )}

        {tab === 'invitations' && (
          <div data-testid="invitations-panel">
            {isAdmin ? (
              <form onSubmit={handleSendInvite} className="space-y-3">
                <label className="label" htmlFor="invite-username">
                  <span className="label-text">Invite by username</span>
                </label>
                <div className="flex gap-2">
                  <input
                    id="invite-username"
                    data-testid="invite-username"
                    className="input input-bordered flex-1"
                    value={inviteUsername}
                    onChange={(e) => setInviteUsername(e.target.value)}
                    placeholder="username"
                    disabled={inviteMut.isPending}
                  />
                  <button
                    type="submit"
                    className="btn btn-primary"
                    data-testid="invite-send"
                    disabled={inviteMut.isPending}
                  >
                    {inviteMut.isPending ? 'Sending...' : 'Send invite'}
                  </button>
                </div>
                {inviteError && (
                  <div className="alert alert-error text-sm" data-testid="invite-error">{inviteError}</div>
                )}
                {inviteSuccess && (
                  <div className="alert alert-success text-sm" data-testid="invite-success">{inviteSuccess}</div>
                )}
              </form>
            ) : (
              <div className="text-sm text-base-content/60">Only admins can send invitations.</div>
            )}
          </div>
        )}

        {tab === 'settings' && (
          <form data-testid="settings-panel" className="space-y-4" onSubmit={handleSaveSettings}>
            <div>
              <label className="label" htmlFor="settings-name">
                <span className="label-text">Room name</span>
              </label>
              <input
                id="settings-name"
                data-testid="settings-name"
                className="input input-bordered w-full"
                value={settingsName}
                onChange={(e) => setSettingsName(e.target.value)}
                disabled={!isOwner || updateMut.isPending}
                required
              />
            </div>
            <div>
              <label className="label" htmlFor="settings-description">
                <span className="label-text">Description</span>
              </label>
              <textarea
                id="settings-description"
                data-testid="settings-description"
                className="textarea textarea-bordered w-full"
                value={settingsDescription}
                onChange={(e) => setSettingsDescription(e.target.value)}
                disabled={!isOwner || updateMut.isPending}
                rows={3}
              />
            </div>
            <div>
              <div className="label">
                <span className="label-text">Visibility</span>
              </div>
              <div className="flex gap-4">
                <label className="label cursor-pointer gap-2">
                  <input
                    type="radio"
                    name="settings-visibility"
                    className="radio"
                    data-testid="settings-visibility-public"
                    checked={settingsVisibility === 0}
                    onChange={() => setSettingsVisibility(0 as RoomVisibilityValue)}
                    disabled={!isOwner || updateMut.isPending}
                  />
                  <span className="label-text">Public</span>
                </label>
                <label className="label cursor-pointer gap-2">
                  <input
                    type="radio"
                    name="settings-visibility"
                    className="radio"
                    data-testid="settings-visibility-private"
                    checked={settingsVisibility === 1}
                    onChange={() => setSettingsVisibility(1 as RoomVisibilityValue)}
                    disabled={!isOwner || updateMut.isPending}
                  />
                  <span className="label-text">Private</span>
                </label>
              </div>
            </div>
            {settingsError && (
              <div className="alert alert-error text-sm" data-testid="settings-error">{settingsError}</div>
            )}
            {settingsSaved && !settingsError && (
              <div className="alert alert-success text-sm" data-testid="settings-saved">Changes saved.</div>
            )}
            <div className="flex items-center justify-between pt-4 border-t border-base-300">
              <button
                type="submit"
                className="btn btn-primary"
                disabled={!isOwner || updateMut.isPending}
                data-testid="settings-save"
              >
                {updateMut.isPending ? 'Saving...' : 'Save changes'}
              </button>
              {isOwner && (
                <button
                  type="button"
                  className="btn btn-error"
                  onClick={handleDelete}
                  disabled={deleteMut.isPending}
                  data-testid="settings-delete-room"
                >
                  {deleteMut.isPending ? 'Deleting...' : 'Delete room'}
                </button>
              )}
            </div>
          </form>
        )}
      </div>
      <div className="modal-backdrop" onClick={onClose} />
    </dialog>
  )
}

interface MemberRowProps {
  m: RoomMember
  isOwner: boolean
  isAdmin: boolean
  onMakeAdmin: () => void
  onRemoveAdmin: () => void
  onBan: () => void
  onRemove: () => void
}

function MemberRow({ m, isOwner, isAdmin, onMakeAdmin, onRemoveAdmin, onBan, onRemove }: MemberRowProps) {
  const status = usePresence(m.userId)
  const statusLabel = status === 'online' ? 'Online' : status === 'afk' ? 'AFK' : 'Offline'
  const roleLabel = m.role === RoomRole.Owner ? 'Owner' : m.role === RoomRole.Admin ? 'Admin' : 'Member'
  return (
    <tr data-testid={`members-row-${m.userId}`}>
      <td>
        <div className="font-medium">{m.displayName || m.userName}</div>
        <div className="text-xs text-base-content/60">@{m.userName}</div>
      </td>
      <td data-testid={`member-status-${m.userId}`}>
        <span className="inline-flex items-center gap-2">
          <PresenceDot userId={m.userId} />
          <span className="text-sm">{statusLabel}</span>
        </span>
      </td>
      <td data-testid={`member-role-${m.userId}`}>{roleLabel}</td>
      <td>
        <div className="flex gap-2">
          {isOwner && m.role === RoomRole.Member && (
            <button className="btn btn-xs" onClick={onMakeAdmin}>Make admin</button>
          )}
          {isOwner && m.role === RoomRole.Admin && (
            <button className="btn btn-xs" onClick={onRemoveAdmin}>Remove admin</button>
          )}
          {isAdmin && m.role !== RoomRole.Owner && (
            <>
              <button className="btn btn-xs btn-warning" onClick={onBan}>Ban</button>
              <button className="btn btn-xs btn-error" onClick={onRemove}>Remove</button>
            </>
          )}
        </div>
      </td>
    </tr>
  )
}
