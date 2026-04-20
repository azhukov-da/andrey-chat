import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { LoginFormSchema, type LoginForm } from '@/types'
import { useAuth } from '@/hooks/useAuth'

export default function SignIn() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const returnTo = params.get('returnTo') ?? '/rooms'
  const [error, setError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginForm>({ resolver: zodResolver(LoginFormSchema) })

  const onSubmit = async (data: LoginForm) => {
    setError(null)
    try {
      await login(data.email, data.password, data.keepSignedIn ?? false)
      navigate(returnTo)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Login failed')
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-base-200">
      <div className="card w-full max-w-sm bg-base-100 shadow-xl">
        <div className="card-body">
          <h2 className="card-title text-2xl mb-2">Sign In</h2>
          {error && <div className="alert alert-error text-sm">{error}</div>}
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-3">
            <div className="form-control">
              <label className="label"><span className="label-text">Email</span></label>
              <input type="email" className="input input-bordered" {...register('email')} />
              {errors.email && <label className="label"><span className="label-text-alt text-error">{errors.email.message}</span></label>}
            </div>
            <div className="form-control">
              <label className="label"><span className="label-text">Password</span></label>
              <input type="password" className="input input-bordered" {...register('password')} />
              {errors.password && <label className="label"><span className="label-text-alt text-error">{errors.password.message}</span></label>}
            </div>
            <div className="form-control">
              <label className="label cursor-pointer justify-start gap-3">
                <input type="checkbox" className="checkbox checkbox-sm" {...register('keepSignedIn')} />
                <span className="label-text">Keep me signed in</span>
              </label>
            </div>
            <button type="submit" className="btn btn-primary w-full" disabled={isSubmitting}>
              {isSubmitting ? <span className="loading loading-spinner" /> : 'Sign In'}
            </button>
          </form>
          <div className="text-sm text-center mt-2 space-y-1">
            <p><Link to="/forgot" className="link link-primary">Forgot password?</Link></p>
            <p>No account? <Link to="/register" className="link link-primary">Register</Link></p>
          </div>
        </div>
      </div>
    </div>
  )
}
