import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useNavigate } from 'react-router'
import { RegisterFormSchema, type RegisterForm } from '@/types'
import { useAuth } from '@/hooks/useAuth'

export default function Register() {
  const { register: registerUser, login } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<RegisterForm>({ resolver: zodResolver(RegisterFormSchema) })

  const onSubmit = async (data: RegisterForm) => {
    setError(null)
    try {
      await registerUser(data.email, data.password)
      await login(data.email, data.password, false)
      navigate('/rooms')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Registration failed')
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-base-200">
      <div className="card w-full max-w-sm bg-base-100 shadow-xl">
        <div className="card-body">
          <h2 className="card-title text-2xl mb-2">Create Account</h2>
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
              <label className="label"><span className="label-text">Confirm Password</span></label>
              <input type="password" className="input input-bordered" {...register('confirmPassword')} />
              {errors.confirmPassword && <label className="label"><span className="label-text-alt text-error">{errors.confirmPassword.message}</span></label>}
            </div>
            <button type="submit" className="btn btn-primary w-full" disabled={isSubmitting}>
              {isSubmitting ? <span className="loading loading-spinner" /> : 'Create Account'}
            </button>
          </form>
          <p className="text-sm text-center mt-2">
            Already have an account? <Link to="/login" className="link link-primary">Sign In</Link>
          </p>
        </div>
      </div>
    </div>
  )
}
