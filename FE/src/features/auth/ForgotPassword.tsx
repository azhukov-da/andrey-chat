import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router'
import { ForgotPasswordFormSchema, type ForgotPasswordForm } from '@/types'
import { forgotPassword } from '@/api/auth'

export default function ForgotPassword() {
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ForgotPasswordForm>({ resolver: zodResolver(ForgotPasswordFormSchema) })

  const onSubmit = async (data: ForgotPasswordForm) => {
    setError(null)
    try {
      await forgotPassword(data.email)
      setSent(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Request failed')
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-base-200">
      <div className="card w-full max-w-sm bg-base-100 shadow-xl">
        <div className="card-body">
          <h2 className="card-title text-2xl mb-2">Forgot Password</h2>
          {sent ? (
            <div className="alert alert-success">Check your email for the reset link.</div>
          ) : (
            <>
              {error && <div className="alert alert-error text-sm">{error}</div>}
              <form onSubmit={handleSubmit(onSubmit)} className="space-y-3">
                <div className="form-control">
                  <label className="label"><span className="label-text">Email</span></label>
                  <input type="email" className="input input-bordered" {...register('email')} />
                  {errors.email && <label className="label"><span className="label-text-alt text-error">{errors.email.message}</span></label>}
                </div>
                <button type="submit" className="btn btn-primary w-full" disabled={isSubmitting}>
                  {isSubmitting ? <span className="loading loading-spinner" /> : 'Send Reset Link'}
                </button>
              </form>
            </>
          )}
          <p className="text-sm text-center mt-2">
            <Link to="/login" className="link link-primary">Back to Sign In</Link>
          </p>
        </div>
      </div>
    </div>
  )
}
