import { CommunityIssue, CommunityComment, NewIssuePayload } from '../../types/community';

const DEFAULT_WORKER_URL = 'https://jellyemu-community.grimmdev.workers.dev';

export async function compressImageFile(file: File): Promise<File | Blob> {
  if (!file || !file.type || !file.type.startsWith('image/')) return file;
  if (file.size <= 1024 * 1024) return file;

  return new Promise((resolve) => {
    const reader = new FileReader();
    reader.onload = (e) => {
      const img = new Image();
      img.onload = () => {
        const maxW = 1920;
        const maxH = 1080;
        let w = img.width;
        let h = img.height;

        if (w > maxW || h > maxH) {
          if (w / h > maxW / maxH) {
            h = Math.round((h * maxW) / w);
            w = maxW;
          } else {
            w = Math.round((w * maxH) / h);
            h = maxH;
          }
        }

        const canvas = document.createElement('canvas');
        canvas.width = w;
        canvas.height = h;
        const ctx = canvas.getContext('2d');
        if (!ctx) {
          resolve(file);
          return;
        }
        ctx.drawImage(img, 0, 0, w, h);

        canvas.toBlob((blob) => {
          if (!blob) {
            resolve(file);
            return;
          }
          const compFile = new File([blob], file.name.replace(/\.[^/.]+$/, '') + '.jpg', {
            type: 'image/jpeg',
            lastModified: Date.now()
          });
          resolve(compFile);
        }, 'image/jpeg', 0.85);
      };
      img.onerror = () => resolve(file);
      img.src = e.target?.result as string;
    };
    reader.onerror = () => resolve(file);
    reader.readAsDataURL(file);
  });
}

export const communityService = {
  getInstanceId: (): string => {
    let id = localStorage.getItem('je_instance_id');
    if (!id) {
      id = 'inst_' + Math.random().toString(36).substring(2, 10) + Date.now().toString(36);
      localStorage.setItem('je_instance_id', id);
    }
    return id;
  },

  getUsername: (): string => {
    const cached = localStorage.getItem('je_username');
    if (cached && cached.trim()) return cached.trim();
    const inst = communityService.getInstanceId();
    return 'Gamer-' + inst.substring(inst.length - 5);
  },

  setUsername: (name: string): void => {
    if (name?.trim()) localStorage.setItem('je_username', name.trim());
  },

  uploadImage: async (file: File): Promise<string | null> => {
    if (!file) return null;
    if (file.size > 10 * 1024 * 1024) {
      throw new Error('Image file size exceeds 10MB limit.');
    }

    const compFile = await compressImageFile(file);
    const formData = new FormData();
    formData.append('file', compFile, file.name || 'screenshot.jpg');

    const res = await fetch(`${DEFAULT_WORKER_URL}/api/upload`, {
      method: 'POST',
      body: formData
    });
    if (!res.ok) throw new Error(`Upload failed with HTTP ${res.status}`);
    const data = await res.json();
    return data?.url || null;
  },

  getIssues: async (): Promise<CommunityIssue[]> => {
    try {
      const res = await fetch(`${DEFAULT_WORKER_URL}/api/issues`, {
        headers: { 'Content-Type': 'application/json' }
      });
      if (!res.ok) throw new Error(`HTTP error ${res.status}`);
      const data = await res.json();
      const myInst = communityService.getInstanceId();

      const items: any[] = Array.isArray(data) ? data : (data?.issues || data?.items || []);
      return items.map((item: any) => {
        const isUpvoted = Array.isArray(item.issue_votes)
          ? item.issue_votes.some((v: any) => v.instance_id === myInst || v === myInst)
          : Array.isArray(item.upvoters)
          ? item.upvoters.includes(myInst)
          : false;

        const upvoteCount = typeof item.upvotes === 'number'
          ? item.upvotes
          : Array.isArray(item.issue_votes)
          ? item.issue_votes.length
          : typeof item.likes_count === 'number'
          ? item.likes_count
          : typeof item.likes === 'number'
          ? item.likes
          : (parseInt(item.upvotes, 10) || 0);

        const replyCount = typeof item.replies_count === 'number'
          ? item.replies_count
          : typeof item.comments_count === 'number'
          ? item.comments_count
          : Array.isArray(item.replies)
          ? item.replies.length
          : Array.isArray(item.comments)
          ? item.comments.length
          : (parseInt(item.replies_count ?? item.comments_count, 10) || 0);

        return {
          id: item.id?.toString() || item.number?.toString() || Math.random().toString(),
          title: item.title || 'Untitled Discussion',
          description: item.description || item.body || item.summary || '',
          category: item.category || 'General',
          status: item.status || 'open',
          author: item.username || item.author || 'Gamer',
          username: item.username || item.author || 'Gamer',
          instance_id: item.instance_id,
          created_at: item.created_at || item.created || new Date().toISOString(),
          upvotes: upvoteCount,
          upvotedByMe: isUpvoted,
          comments_count: replyCount,
          image_url: item.image_url,
          is_poll: !!item.is_poll || !!item.poll,
          poll_options: item.poll_options || item.poll?.options?.nodes
        };
      });
    } catch (err) {
      console.warn('[JellyEmu] Live community fetch error, returning empty list:', err);
      return [];
    }
  },

  createIssue: async (payload: NewIssuePayload): Promise<boolean> => {
    try {
      const res = await fetch(`${DEFAULT_WORKER_URL}/api/issues`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      return res.ok;
    } catch (err) {
      console.error('[JellyEmu] Create issue failed:', err);
      return false;
    }
  },

  toggleUpvote: async (issueId: string): Promise<boolean> => {
    try {
      const res = await fetch(`${DEFAULT_WORKER_URL}/api/issues/${issueId}/upvote`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ instance_id: communityService.getInstanceId() })
      });
      return res.ok;
    } catch {
      return false;
    }
  },

  getReplies: async (issueId: string): Promise<CommunityComment[]> => {
    try {
      const res = await fetch(`${DEFAULT_WORKER_URL}/api/issues/${issueId}/replies`);
      if (!res.ok) return [];
      const data = await res.json();
      const items: any[] = Array.isArray(data) ? data : (data?.replies || data?.items || []);
      return items.map((r: any) => ({
        id: r.id?.toString() || Math.random().toString(),
        issue_id: r.issue_id || issueId,
        author: r.username || r.author || 'Gamer',
        username: r.username || r.author || 'Gamer',
        instance_id: r.instance_id,
        content: r.message || r.content || r.body || '',
        created_at: r.created_at || r.created || new Date().toISOString(),
        image_url: r.image_url
      }));
    } catch {
      return [];
    }
  },

  postReply: async (issueId: string, content: string, imageUrl?: string): Promise<boolean> => {
    try {
      const res = await fetch(`${DEFAULT_WORKER_URL}/api/issues/${issueId}/replies`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          instance_id: communityService.getInstanceId(),
          username: communityService.getUsername(),
          message: content,
          content: content,
          image_url: imageUrl || null
        })
      });
      return res.ok;
    } catch {
      return false;
    }
  },

  editIssue: async (issueId: string, title: string, description: string, imageUrl?: string): Promise<boolean> => {
    try {
      const res = await fetch(`${DEFAULT_WORKER_URL}/api/issues/${issueId}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          title,
          description,
          instance_id: communityService.getInstanceId(),
          image_url: imageUrl
        })
      });
      return res.ok;
    } catch {
      return false;
    }
  },

  deleteIssue: async (issueId: string): Promise<boolean> => {
    try {
      const myInst = communityService.getInstanceId();
      const res = await fetch(`${DEFAULT_WORKER_URL}/api/issues/${issueId}?instance_id=${myInst}`, {
        method: 'DELETE'
      });
      return res.ok;
    } catch {
      return false;
    }
  },

  editReply: async (replyId: string, message: string, imageUrl?: string): Promise<boolean> => {
    try {
      const res = await fetch(`${DEFAULT_WORKER_URL}/api/replies/${replyId}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          message,
          content: message,
          instance_id: communityService.getInstanceId(),
          image_url: imageUrl
        })
      });
      return res.ok;
    } catch {
      return false;
    }
  },

  deleteReply: async (replyId: string): Promise<boolean> => {
    try {
      const myInst = communityService.getInstanceId();
      const res = await fetch(`${DEFAULT_WORKER_URL}/api/replies/${replyId}?instance_id=${myInst}`, {
        method: 'DELETE'
      });
      return res.ok;
    } catch {
      return false;
    }
  }
};
