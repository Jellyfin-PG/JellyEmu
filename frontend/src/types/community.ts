export interface CommunityPollOption {
  id?: string;
  text: string;
  votes: number;
  votedByMe?: boolean;
}

export interface CommunityComment {
  id: string;
  issue_id: string;
  author: string;
  username?: string;
  instance_id?: string;
  content: string;
  created_at: string;
  image_url?: string;
}

export interface CommunityIssue {
  id: string;
  title: string;
  description: string;
  category: string;
  status: 'open' | 'in_progress' | 'resolved' | 'closed';
  author: string;
  username?: string;
  instance_id?: string;
  created_at: string;
  updated_at?: string;
  upvotes?: number;
  upvotedByMe?: boolean;
  comments_count?: number;
  image_url?: string;
  is_poll?: boolean;
  poll_options?: CommunityPollOption[];
  replies?: CommunityComment[];
}

export interface NewIssuePayload {
  title: string;
  description: string;
  category: string;
  username: string;
  instance_id: string;
  image_url?: string;
  is_poll?: boolean;
  poll_options?: string[];
}
