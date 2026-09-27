export interface ConnectedRepository {
  id: string;
  githubRepositoryId: number;
  fullName: string;
  owner: string;
  name: string;
  defaultBranch: string;
  installationId?: number | null;
  isActive: boolean;
  createdAt: string;
}

export interface AvailableRepository {
  id: number;
  fullName: string;
  owner: string;
  name: string;
  defaultBranch: string;
  isConnected: boolean;
}
