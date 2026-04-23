import { z } from 'zod'

export const AccessTokenResponseSchema = z.object({
  tokenType: z.string().nullable().optional(),
  accessToken: z.string(),
  expiresIn: z.number(),
  refreshToken: z.string(),
})
export type AccessTokenResponse = z.infer<typeof AccessTokenResponseSchema>

export const UserProfileSchema = z.object({
  id: z.string(),
  userName: z.string(),
  displayName: z.string().nullable().optional(),
  email: z.string().nullable().optional(),
  createdAt: z.string(),
})
export type UserProfile = z.infer<typeof UserProfileSchema>

export const RoomVisibility = { Public: 0, Private: 1 } as const
export type RoomVisibilityValue = (typeof RoomVisibility)[keyof typeof RoomVisibility]

export const RoomKind = { Group: 0, Direct: 1 } as const
export type RoomKindValue = (typeof RoomKind)[keyof typeof RoomKind]

export const RoomRole = { Member: 0, Admin: 1, Owner: 2 } as const
export type RoomRoleValue = (typeof RoomRole)[keyof typeof RoomRole]

export const RoomSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable().optional(),
  visibility: z.number(),
  kind: z.number(),
  ownerId: z.string().nullable().optional(),
  ownerUserName: z.string().nullable().optional(),
  otherUserId: z.string().nullable().optional(),
  isFrozen: z.boolean(),
  createdAt: z.string(),
  memberCount: z.number(),
  myRole: z.number().nullable().optional(),
})
export type Room = z.infer<typeof RoomSchema>

export const RoomMemberSchema = z.object({
  userId: z.string(),
  userName: z.string(),
  displayName: z.string().nullable().optional(),
  role: z.number(),
  joinedAt: z.string(),
})
export type RoomMember = z.infer<typeof RoomMemberSchema>

export const AttachmentMetadataSchema = z.object({
  id: z.string().uuid(),
  fileName: z.string(),
  contentType: z.string(),
  sizeBytes: z.number(),
  kind: z.string(),
  comment: z.string().nullable().optional(),
})
export type AttachmentMetadata = z.infer<typeof AttachmentMetadataSchema>

export const MessageSchema = z.object({
  id: z.string().uuid(),
  roomId: z.string().uuid(),
  authorId: z.string(),
  authorUserName: z.string(),
  authorDisplayName: z.string().nullable().optional(),
  text: z.string(),
  replyToMessageId: z.string().uuid().nullable().optional(),
  editedAt: z.string().nullable().optional(),
  isDeleted: z.boolean(),
  createdAt: z.string(),
  attachments: z.array(AttachmentMetadataSchema),
})
export type Message = z.infer<typeof MessageSchema>

export const FriendshipStatus = { Pending: 0, Accepted: 1 } as const
export type FriendshipStatusValue = (typeof FriendshipStatus)[keyof typeof FriendshipStatus]

export const FriendSchema = z.object({
  userId: z.string(),
  userName: z.string(),
  displayName: z.string().nullable().optional(),
  status: z.number(),
  createdAt: z.string(),
  acceptedAt: z.string().nullable().optional(),
})
export type Friend = z.infer<typeof FriendSchema>

export const PagedSchema = <T extends z.ZodTypeAny>(item: T) =>
  z.object({
    items: z.array(item),
    page: z.number(),
    pageSize: z.number(),
    totalCount: z.number(),
  })
export type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number }

export const CursorPagedSchema = <T extends z.ZodTypeAny>(item: T) =>
  z.object({
    items: z.array(item),
    nextCursor: z.string().uuid().nullable().optional(),
    hasMore: z.boolean(),
  })
export type CursorPaged<T> = { items: T[]; nextCursor?: string | null; hasMore: boolean }

// Form schemas
export const RegisterFormSchema = z
  .object({
    email: z.string().email('Invalid email'),
    username: z
      .string()
      .min(3, 'Username must be at least 3 characters')
      .max(32, 'Username must be at most 32 characters')
      .regex(/^[A-Za-z0-9._-]+$/, 'Username can contain letters, digits, . _ -'),
    password: z.string().superRefine((val, ctx) => {
      const missing: string[] = []
      if (val.length < 6) missing.push('at least 6 characters')
      if (!/[A-Z]/.test(val)) missing.push('an uppercase letter')
      if (!/[0-9]/.test(val)) missing.push('a digit')
      if (missing.length > 0)
        ctx.addIssue({ code: z.ZodIssueCode.custom, message: `Password needs: ${missing.join(', ')}` })
    }),
    confirmPassword: z.string(),
  })
  .refine((d) => d.password === d.confirmPassword, {
    message: 'Passwords do not match',
    path: ['confirmPassword'],
  })
  .refine((d) => d.username.toLowerCase() !== d.email.toLowerCase(), {
    message: 'Username must be different from email',
    path: ['username'],
  })
export type RegisterForm = z.infer<typeof RegisterFormSchema>

export const LoginFormSchema = z.object({
  email: z.string().email('Invalid email'),
  password: z.string().min(1, 'Required'),
  keepSignedIn: z.boolean().optional(),
})
export type LoginForm = z.infer<typeof LoginFormSchema>

export const ForgotPasswordFormSchema = z.object({
  email: z.string().email('Invalid email'),
})
export type ForgotPasswordForm = z.infer<typeof ForgotPasswordFormSchema>

export const ResetPasswordFormSchema = z
  .object({
    newPassword: z
      .string()
      .min(6, 'At least 6 characters')
      .regex(/[A-Z]/, 'Needs an uppercase letter')
      .regex(/[0-9]/, 'Needs a digit'),
    confirmPassword: z.string(),
  })
  .refine((d) => d.newPassword === d.confirmPassword, {
    message: 'Passwords do not match',
    path: ['confirmPassword'],
  })
export type ResetPasswordForm = z.infer<typeof ResetPasswordFormSchema>

export const CreateRoomFormSchema = z.object({
  name: z.string().min(1, 'Required').max(100),
  description: z.string().max(500).optional(),
  visibility: z.enum(['0', '1']),
})
export type CreateRoomForm = z.infer<typeof CreateRoomFormSchema>

export const MAX_MESSAGE_BYTES = 3072

export const SendMessageFormSchema = z.object({
  text: z.string().min(1).refine((t) => new TextEncoder().encode(t).length <= MAX_MESSAGE_BYTES, {
    message: `Message too long (max ${MAX_MESSAGE_BYTES} bytes)`,
  }),
})
export type SendMessageForm = z.infer<typeof SendMessageFormSchema>

export type PresenceStatus = 'online' | 'afk' | 'offline'

export interface OptimisticMessage extends Message {
  _status?: 'sending' | 'failed'
  _nonce?: string
}
