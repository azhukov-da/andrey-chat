import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useSearchParams, useNavigate } from 'react-router'
import { ResetPasswordFormSchema, type ResetPasswordForm } from '@/types'
import { resetPassword } from '@/api/auth'

export default function ResetPassword() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const email = params.get('email') ?? ''
  const token = params.get('token') ?? ''
  const [error, setError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ResetPasswordForm>({ resolver: zodResolver(ResetPasswordFormSchema) })

  const onSubmit = async (data: ResetPasswordForm) => {
    setError(null)
    try {
      await resetPassword(email, token, data.newPassword)
      navigate('/login')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Reset failed')
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-base-200">
      <div className="card w-full max-w-sm bg-base-100 shadow-xl">
        <div className="card-body">
          <h2 className="card-title text-2xl mb-2">Reset Password</h2>
          {error && <div className="alert alert-error text-sm">{error}</div>}
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-3">
            <div className="form-control">
              <label className="label"><span className="label-text">New Password</span></label>
              <input type="password" className="input input-bordered" {...register('newPassword')} />
              {errors.newPassword && <label className="label"><span className="label-text-alt text-error">{errors.newPassword.message}</span></label>}
            </div>
            <div className="form-control">
              <label className="label"><span className="label-text">Confirm Password</span></label>
              <input type="password" className="input input-bordered" {...register('confirmPassword')} />
              {errors.confirmPassword && <label className="label"><span className="label-text-alt text-error">{errors.confirmPassword.message}</span></label>}
            </div>
            <button type="submit" className="btn btn-primary w-full" disabled={isSubmitting}>
              {isSubmitting ? <span className="loading loading-spinner" /> : 'Reset Password'}
            </button>
          </form>
          <p className="text-sm text-center mt-2">
            <Link to="/login" className="link link-primary">Back to Sign In</Link>
          </p>
        </div>
      </div>
    </div>
  )
}
