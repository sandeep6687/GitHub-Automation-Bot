export interface UserProfile {
  id: string;
  githubUserId: number;
  login: string;
  email?: string;
  avatarUrl?: string;
  name?: string;
}
