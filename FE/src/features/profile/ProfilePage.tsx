import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { updateDisplayName, deleteAccount } from '@/api/me'
import { useAuthStore } from '@/stores/authStore'
import { useAuth } from '@/hooks/useAuth'

const DisplayNameSchema = z.object({ displayName: z.string().max(50) })
type DisplayNameForm = z.infer<typeof DisplayNameSchema>

export default function ProfilePage() {
  const me = useAuthStore((s) => s.me)
  const { logout } = useAuth()
  const queryClient = useQueryClient()
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false)

  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<DisplayNameForm>({
    resolver: zodResolver(DisplayNameSchema),
    defaultValues: { displayName: me?.displayName ?? '' },
  })

  const updateMutation = useMutation({
    mutationFn: (data: DisplayNameForm) => updateDisplayName(data.displayName),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me'] }),
  })

  const deleteMutation = useMutation({
    mutationFn: () => deleteAccount().then(() => void 0),
    onSuccess: () => logout(),
  })

  if (!me) return null

  return (
    <div className="p-6 max-w-lg mx-auto">
      <h1 className="text-2xl font-bold mb-6">Profile</h1>

      <div className="card bg-base-100 shadow mb-4">
        <div className="card-body">
          <h2 className="card-title text-base">Account Info</h2>
          <p className="text-sm text-base-content/60">Username: <span className="font-mono">{me.userName}</span></p>
          {me.email && <p className="text-sm text-base-content/60">Email: {me.email}</p>}
        </div>
      </div>

      <div className="card bg-base-100 shadow mb-4">
        <div className="card-body">
          <h2 className="card-title text-base">Display Name</h2>
          <form onSubmit={handleSubmit((d) => updateMutation.mutate(d))} className="flex gap-2">
            <input
              className="input input-bordered flex-1"
              placeholder="Display name (optional)"
              {...register('displayName')}
            />
            <button type="submit" className="btn btn-primary" disabled={isSubmitting || updateMutation.isPending}>
              Save
            </button>
          </form>
          {errors.displayName && <p className="text-error text-sm">{errors.displayName.message}</p>}
          {updateMutation.isSuccess && <p className="text-success text-sm">Saved!</p>}
        </div>
      </div>

      <div className="card bg-base-100 shadow border border-error/30">
        <div className="card-body">
          <h2 className="card-title text-base text-error">Danger Zone</h2>
          {!showDeleteConfirm ? (
            <button className="btn btn-error btn-outline btn-sm w-fit" onClick={() => setShowDeleteConfirm(true)}>
              Delete Account
            </button>
          ) : (
            <div className="space-y-2">
              <p className="text-sm text-error">This will permanently delete your account. Are you sure?</p>
              <div className="flex gap-2">
                <button className="btn btn-error btn-sm" onClick={() => deleteMutation.mutate()} disabled={deleteMutation.isPending}>
                  {deleteMutation.isPending ? <span className="loading loading-spinner loading-xs" /> : 'Yes, Delete'}
                </button>
                <button className="btn btn-ghost btn-sm" onClick={() => setShowDeleteConfirm(false)}>Cancel</button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
