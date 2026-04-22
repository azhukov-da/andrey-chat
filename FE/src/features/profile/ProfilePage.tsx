import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { updateDisplayName, deleteAccount, changePassword } from '@/api/me'
import { useAuthStore } from '@/stores/authStore'
import { useAuth } from '@/hooks/useAuth'

const DisplayNameSchema = z.object({ displayName: z.string().max(50) })
type DisplayNameForm = z.infer<typeof DisplayNameSchema>

const PasswordSchema = z.object({
  currentPassword: z.string().min(1, 'Current password is required'),
  newPassword: z.string().min(6, 'New password must be at least 6 characters'),
  confirmPassword: z.string(),
}).refine((d) => d.newPassword === d.confirmPassword, {
  message: 'Passwords do not match',
  path: ['confirmPassword'],
})
type PasswordForm = z.infer<typeof PasswordSchema>

export default function ProfilePage() {
  const me = useAuthStore((s) => s.me)
  const { logout } = useAuth()
  const queryClient = useQueryClient()
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false)

  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<DisplayNameForm>({
    resolver: zodResolver(DisplayNameSchema),
    defaultValues: { displayName: me?.displayName ?? '' },
  })

  const pwForm = useForm<PasswordForm>({
    resolver: zodResolver(PasswordSchema),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  })

  const [pwError, setPwError] = useState<string | null>(null)
  const passwordMutation = useMutation({
    mutationFn: (d: PasswordForm) => changePassword(d.currentPassword, d.newPassword),
    onSuccess: () => {
      setPwError(null)
      pwForm.reset()
    },
    onError: (e: Error) => setPwError(e.message),
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

      <div className="card bg-base-100 shadow mb-4">
        <div className="card-body">
          <h2 className="card-title text-base">Change Password</h2>
          <form onSubmit={pwForm.handleSubmit((d) => passwordMutation.mutate(d))} className="space-y-2">
            <input
              type="password"
              className="input input-bordered w-full"
              placeholder="Current password"
              autoComplete="current-password"
              {...pwForm.register('currentPassword')}
            />
            {pwForm.formState.errors.currentPassword && (
              <p className="text-error text-sm">{pwForm.formState.errors.currentPassword.message}</p>
            )}
            <input
              type="password"
              className="input input-bordered w-full"
              placeholder="New password"
              autoComplete="new-password"
              {...pwForm.register('newPassword')}
            />
            {pwForm.formState.errors.newPassword && (
              <p className="text-error text-sm">{pwForm.formState.errors.newPassword.message}</p>
            )}
            <input
              type="password"
              className="input input-bordered w-full"
              placeholder="Confirm new password"
              autoComplete="new-password"
              {...pwForm.register('confirmPassword')}
            />
            {pwForm.formState.errors.confirmPassword && (
              <p className="text-error text-sm">{pwForm.formState.errors.confirmPassword.message}</p>
            )}
            {pwError && <p className="text-error text-sm">{pwError}</p>}
            {passwordMutation.isSuccess && <p className="text-success text-sm">Password changed!</p>}
            <button type="submit" className="btn btn-primary" disabled={passwordMutation.isPending}>
              {passwordMutation.isPending ? <span className="loading loading-spinner loading-xs" /> : 'Change Password'}
            </button>
          </form>
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
