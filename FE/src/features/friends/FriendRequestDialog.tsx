import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { sendFriendRequest } from '@/api/friends'

interface Props {
  onClose: () => void
}

export default function FriendRequestDialog({ onClose }: Props) {
  const [identifier, setIdentifier] = useState('')
  const [message, setMessage] = useState('')
  const queryClient = useQueryClient()

  const mutation = useMutation({
    mutationFn: () => sendFriendRequest(identifier.trim(), message.trim() || undefined),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['friends'] })
      onClose()
    },
  })

  return (
    <dialog open className="modal modal-open">
      <div className="modal-box">
        <h3 className="font-bold text-lg mb-4">Add Contact</h3>
        <div className="space-y-3">
          <div className="form-control">
            <label className="label"><span className="label-text">Username or email</span></label>
            <input
              className="input input-bordered"
              value={identifier}
              onChange={(e) => setIdentifier(e.target.value)}
              placeholder="Enter username or email"
            />
          </div>
          <div className="form-control">
            <label className="label"><span className="label-text">Message (optional)</span></label>
            <input
              className="input input-bordered"
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              placeholder="Hi, I'd like to connect!"
            />
          </div>
          {mutation.isError && (
            <div className="alert alert-error text-sm">{(mutation.error as Error).message}</div>
          )}
        </div>
        <div className="modal-action">
          <button className="btn btn-ghost" onClick={onClose}>Cancel</button>
          <button
            className="btn btn-primary"
            disabled={!identifier.trim() || mutation.isPending}
            onClick={() => mutation.mutate()}
          >
            {mutation.isPending ? <span className="loading loading-spinner loading-xs" /> : 'Send Request'}
          </button>
        </div>
      </div>
      <div className="modal-backdrop" onClick={onClose} />
    </dialog>
  )
}
