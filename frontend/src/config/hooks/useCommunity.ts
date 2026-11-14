import { useState, useEffect, useCallback } from 'react';
import { communityService } from '../services/communityService';
import { CommunityIssue, NewIssuePayload } from '../../types/community';

export function useCommunity() {
  const [issues, setIssues] = useState<CommunityIssue[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [creating, setCreating] = useState<boolean>(false);
  const [username, setUsernameState] = useState<string>(communityService.getUsername());

  const fetchIssues = useCallback(async () => {
    try {
      setLoading(true);
      const data = await communityService.getIssues();
      setIssues(data);
    } catch (err) {
      console.warn('[JellyEmu] Community fetch failed:', err);
      setIssues([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchIssues();
  }, [fetchIssues]);

  const updateUsername = (name: string) => {
    communityService.setUsername(name);
    setUsernameState(name);
  };

  const createIssue = async (title: string, description: string, category: string, imageUrl?: string) => {
    try {
      setCreating(true);
      const payload: NewIssuePayload = {
        title,
        description,
        category,
        username,
        instance_id: communityService.getInstanceId(),
        image_url: imageUrl
      };
      const ok = await communityService.createIssue(payload);
      if (ok) {
        await fetchIssues();
      }
      return ok;
    } catch (err) {
      console.error('[JellyEmu] Create issue failed:', err);
      return false;
    } finally {
      setCreating(false);
    }
  };

  const toggleUpvote = async (issueId: string) => {
    const ok = await communityService.toggleUpvote(issueId);
    if (ok) {
      setIssues(prev =>
        prev.map(i => {
          if (i.id === issueId) {
            const upvoted = !i.upvotedByMe;
            return {
              ...i,
              upvotedByMe: upvoted,
              upvotes: Math.max(0, (i.upvotes || 0) + (upvoted ? 1 : -1))
            };
          }
          return i;
        })
      );
    }
  };

  return {
    issues,
    loading,
    creating,
    username,
    updateUsername,
    createIssue,
    toggleUpvote,
    reload: fetchIssues
  };
}
