import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { CreateRoomFormSchema, type CreateRoomForm, RoomVisibility } from '@/types'
import { createRoom } from '@/api/rooms'

interface Props {
  onClose: () => void
}

export default function CreateRoomDialog({ onClose }: Props) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<CreateRoomForm>({
    resolver: zodResolver(CreateRoomFormSchema),
    defaultValues: { visibility: '0' },
  })

  const mutation = useMutation({
    mutationFn: (data: CreateRoomForm) =>
      createRoom(data.name, data.description, Number(data.visibility) as 0 | 1),
    onSuccess: (room) => {
      void queryClient.invalidateQueries({ queryKey: ['rooms'] })
      navigate(`/rooms/${room.id}`)
      onClose()
    },
  })

  return (
    <dialog open className="modal modal-open">
      <div className="modal-box">
        <h3 className="font-bold text-lg mb-4">Create Room</h3>
        <form
          onSubmit={handleSubmit((d) => mutation.mutate(d))}
          className="space-y-3"
        >
          <div className="form-control">
            <label className="label"><span className="label-text">Name</span></label>
            <input className="input input-bordered" {...register('name')} />
            {errors.name && <label className="label"><span className="label-text-alt text-error">{errors.name.message}</span></label>}
          </div>
          <div className="form-control">
            <label className="label"><span className="label-text">Description</span></label>
            <textarea className="textarea textarea-bordered" {...register('description')} />
          </div>
          <div className="form-control">
            <label className="label"><span className="label-text">Visibility</span></label>
            <select className="select select-bordered" {...register('visibility')}>
              <option value={String(RoomVisibility.Public)}>Public</option>
              <option value={String(RoomVisibility.Private)}>Private</option>
            </select>
          </div>
          {mutation.isError && (
            <div className="alert alert-error text-sm">{(mutation.error as Error).message}</div>
          )}
          <div className="modal-action">
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn btn-primary" disabled={isSubmitting || mutation.isPending}>
              {mutation.isPending ? <span className="loading loading-spinner loading-xs" /> : 'Create'}
            </button>
          </div>
        </form>
      </div>
      <div className="modal-backdrop" onClick={onClose} />
    </dialog>
  )
}
