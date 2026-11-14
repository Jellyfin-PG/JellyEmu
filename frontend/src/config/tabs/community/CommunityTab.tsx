import React, { useState, useEffect, useRef } from 'react';
import { Card, CardBody, Button, Chip, Input, Textarea, Modal, ModalContent, ModalHeader, ModalBody, ModalFooter, useDisclosure, Spinner, Tooltip } from '@heroui/react';
import { MessageSquare, Plus, ThumbsUp, MessageCircle, User, RefreshCw, Send, Image as ImageIcon, Edit3, Trash2, X, ExternalLink, Maximize2, AlertCircle, Save, Check } from 'lucide-react';
import { useCommunity } from '../../hooks/useCommunity';
import { CommunityIssue, CommunityComment } from '../../../types/community';
import { communityService } from '../../services/communityService';
import { CustomSelect } from '../../../shared/CustomSelect';

const ImagePreview: React.FC<{ src?: string; alt?: string; className?: string; onExpand?: (url: string) => void }> = ({ src, alt = '', className = '', onExpand }) => {
  const [hasError, setHasError] = useState(false);
  const [loaded, setLoaded] = useState(false);

  if (!src) return null;

  if (hasError) {
    return (
      <div className="flex items-center justify-between p-2.5 rounded-lg bg-zinc-900/90 border border-white/10 text-xs text-slate-400 gap-2">
        <div className="flex items-center gap-2">
          <AlertCircle className="w-4 h-4 text-warning" />
          <span className="text-slate-300">Attached Screenshot (Link Expired or Unreachable)</span>
        </div>
        <a
          href={src}
          target="_blank"
          rel="noopener noreferrer"
          onClick={(e) => e.stopPropagation()}
          className="text-primary hover:underline text-[11px] flex items-center gap-1 font-medium"
        >
          Direct Link <ExternalLink className="w-3 h-3" />
        </a>
      </div>
    );
  }

  return (
    <div
      onClick={(e) => {
        if (onExpand) {
          e.stopPropagation();
          onExpand(src);
        }
      }}
      className={`relative group/img rounded-lg overflow-hidden bg-black/40 border border-white/10 ${onExpand ? 'cursor-zoom-in' : ''} ${className}`}
    >
      {!loaded && (
        <div className="absolute inset-0 flex items-center justify-center bg-zinc-900/60 text-zinc-500 text-xs gap-1.5">
          <ImageIcon className="w-4 h-4 animate-pulse" />
          <span>Loading image...</span>
        </div>
      )}
      <img
        src={src}
        alt={alt}
        onLoad={() => setLoaded(true)}
        onError={() => setHasError(true)}
        className={`w-full h-full object-cover transition-opacity duration-300 ${loaded ? 'opacity-100' : 'opacity-0'}`}
      />
      {loaded && onExpand && (
        <div className="absolute inset-0 bg-black/40 opacity-0 group-hover/img:opacity-100 transition-opacity flex items-center justify-center gap-2 text-white text-xs font-semibold backdrop-blur-[2px]">
          <Maximize2 className="w-4 h-4" /> Expand Screenshot
        </div>
      )}
    </div>
  );
};

interface EditTarget {
  type: 'issue' | 'reply';
  id: string;
  title?: string;
  content: string;
  imageUrl?: string;
}

export const CommunityTab: React.FC = () => {
  const { issues, loading, creating, username, updateUsername, createIssue, toggleUpvote, reload } = useCommunity();
  const { isOpen, onOpen, onOpenChange } = useDisclosure();
  const { isOpen: isDetailOpen, onOpen: onOpenDetail, onOpenChange: onDetailOpenChange } = useDisclosure();
  const { isOpen: isEditOpen, onOpen: onOpenEdit, onOpenChange: onEditOpenChange, onClose: onCloseEdit } = useDisclosure();
  const { isOpen: isLightboxOpen, onOpen: onOpenLightbox, onOpenChange: onLightboxOpenChange } = useDisclosure();

  const [activeIssue, setActiveIssue] = useState<CommunityIssue | null>(null);
  const [replies, setReplies] = useState<CommunityComment[]>([]);
  const [loadingReplies, setLoadingReplies] = useState<boolean>(false);
  const [replyText, setReplyText] = useState<string>('');
  const [replyImageFile, setReplyImageFile] = useState<File | null>(null);
  const [replyImagePreview, setReplyImagePreview] = useState<string | null>(null);
  const [submittingReply, setSubmittingReply] = useState<boolean>(false);

  const [lightboxImageUrl, setLightboxImageUrl] = useState<string>('');

  const [editTarget, setEditTarget] = useState<EditTarget | null>(null);
  const [editTitle, setEditTitle] = useState<string>('');
  const [editContent, setEditContent] = useState<string>('');
  const [editImageUrl, setEditImageUrl] = useState<string>('');
  const [editImageFile, setEditImageFile] = useState<File | null>(null);
  const [savingEdit, setSavingEdit] = useState<boolean>(false);

  const [newTitle, setNewTitle] = useState('');
  const [newDesc, setNewDesc] = useState('');
  const [newCategory, setNewCategory] = useState('Ideas');
  const [newImageFile, setNewImageFile] = useState<File | null>(null);
  const [newImagePreview, setNewImagePreview] = useState<string | null>(null);
  const [selectedFilter, setSelectedFilter] = useState('all');

  const [handleInput, setHandleInput] = useState<string>(username);
  const [handleSaved, setHandleSaved] = useState<boolean>(false);

  useEffect(() => {
    setHandleInput(username);
  }, [username]);

  const handleSaveUsername = () => {
    const trimmed = handleInput.trim();
    if (!trimmed) return;
    updateUsername(trimmed);
    setHandleSaved(true);
    setTimeout(() => setHandleSaved(false), 2000);
  };

  const newFileInputRef = useRef<HTMLInputElement>(null);
  const replyFileInputRef = useRef<HTMLInputElement>(null);
  const editFileInputRef = useRef<HTMLInputElement>(null);

  const myInstanceId = communityService.getInstanceId();

  const handleOpenLightbox = (url: string) => {
    setLightboxImageUrl(url);
    onOpenLightbox();
  };

  const handleNewImageSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      setNewImageFile(file);
      setNewImagePreview(URL.createObjectURL(file));
    }
  };

  const handleReplyImageSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      setReplyImageFile(file);
      setReplyImagePreview(URL.createObjectURL(file));
    }
  };

  const handleEditImageSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      setEditImageFile(file);
      setEditImageUrl(URL.createObjectURL(file));
    }
  };

  const handleCreate = async (onClose: () => void) => {
    if (!newTitle.trim() || !newDesc.trim()) return;
    let uploadedUrl: string | undefined = undefined;
    if (newImageFile) {
      try {
        uploadedUrl = (await communityService.uploadImage(newImageFile)) || undefined;
      } catch (err: any) {
        alert(err.message || 'Image upload failed.');
        return;
      }
    }

    const ok = await createIssue(newTitle, newDesc, newCategory, uploadedUrl);
    if (ok) {
      setNewTitle('');
      setNewDesc('');
      setNewImageFile(null);
      setNewImagePreview(null);
      onClose();
    }
  };

  const handleOpenDetail = async (issue: CommunityIssue) => {
    setActiveIssue(issue);
    onOpenDetail();
    setLoadingReplies(true);
    try {
      const data = await communityService.getReplies(issue.id);
      setReplies(data);
    } catch {
      setReplies([]);
    } finally {
      setLoadingReplies(false);
    }
  };

  const handleSendReply = async () => {
    if (!activeIssue || !replyText.trim()) return;
    try {
      setSubmittingReply(true);
      let uploadedUrl: string | undefined = undefined;
      if (replyImageFile) {
        uploadedUrl = (await communityService.uploadImage(replyImageFile)) || undefined;
      }

      const ok = await communityService.postReply(activeIssue.id, replyText, uploadedUrl);
      if (ok) {
        setReplyText('');
        setReplyImageFile(null);
        setReplyImagePreview(null);
        const updated = await communityService.getReplies(activeIssue.id);
        setReplies(updated);
        reload();
      }
    } finally {
      setSubmittingReply(false);
    }
  };

  const handleOpenEdit = (target: EditTarget) => {
    setEditTarget(target);
    setEditTitle(target.title || '');
    setEditContent(target.content || '');
    setEditImageUrl(target.imageUrl || '');
    setEditImageFile(null);
    onOpenEdit();
  };

  const handleSaveEdit = async () => {
    if (!editTarget || !editContent.trim()) return;
    try {
      setSavingEdit(true);
      let finalImageUrl = editImageUrl;
      if (editImageFile) {
        finalImageUrl = (await communityService.uploadImage(editImageFile)) || '';
      }

      if (editTarget.type === 'issue') {
        const ok = await communityService.editIssue(editTarget.id, editTitle.trim(), editContent.trim(), finalImageUrl || undefined);
        if (ok) {
          if (activeIssue && activeIssue.id === editTarget.id) {
            setActiveIssue({
              ...activeIssue,
              title: editTitle.trim(),
              description: editContent.trim(),
              image_url: finalImageUrl || undefined
            });
          }
          await reload();
          onCloseEdit();
        }
      } else {
        const ok = await communityService.editReply(editTarget.id, editContent.trim(), finalImageUrl || undefined);
        if (ok) {
          if (activeIssue) {
            const updated = await communityService.getReplies(activeIssue.id);
            setReplies(updated);
          }
          onCloseEdit();
        }
      }
    } finally {
      setSavingEdit(false);
    }
  };

  const handleDeleteIssue = async (issueId: string) => {
    if (!window.confirm('Are you sure you want to delete this post and all its replies? This cannot be undone.')) return;
    const ok = await communityService.deleteIssue(issueId);
    if (ok) {
      if (activeIssue?.id === issueId) {
        onDetailOpenChange();
        setActiveIssue(null);
      }
      await reload();
    }
  };

  const handleDeleteReply = async (replyId: string) => {
    if (!window.confirm('Are you sure you want to delete this reply?')) return;
    const ok = await communityService.deleteReply(replyId);
    if (ok && activeIssue) {
      const updated = await communityService.getReplies(activeIssue.id);
      setReplies(updated);
      await reload();
    }
  };

  const categories = ['all', 'Announcements', 'General', 'Ideas', 'Bugs', 'Polls', 'Q&A', 'Show and Tell'];

  const filteredIssues = issues.filter(issue => {
    if (selectedFilter === 'all') return true;
    return issue.category.toLowerCase() === selectedFilter.toLowerCase();
  });

  if (loading) {
    return (
      <div className="flex items-center justify-center p-16">
        <Spinner label="Connecting to JellyEmu Community Hub..." color="primary" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div>
          <h3 className="text-xl font-bold text-white flex items-center gap-2">
            <MessageSquare className="w-5 h-5 text-primary" />
            JellyEmu Community & Discussions
          </h3>
          <p className="text-sm text-slate-300">Share suggestions, view community patches, and report core compatibility issues.</p>
        </div>

        <div className="flex items-center gap-2.5 flex-wrap">
          <Input
            size="sm"
            placeholder="Gamer Handle"
            value={handleInput}
            onValueChange={setHandleInput}
            onKeyDown={(e) => {
              if (e.key === 'Enter') handleSaveUsername();
            }}
            startContent={<User className="w-3.5 h-3.5 text-slate-400" />}
            endContent={
              <Tooltip content={handleSaved ? "Handle saved!" : "Save Gamer Handle"}>
                <button
                  type="button"
                  onClick={handleSaveUsername}
                  aria-label="Save gamer handle"
                  className={`p-1 rounded-md transition-all ${
                    handleSaved
                      ? 'text-success bg-success/20'
                      : handleInput !== username
                      ? 'text-primary hover:bg-primary/20 bg-primary/10'
                      : 'text-slate-400 hover:text-white'
                  }`}
                >
                  {handleSaved ? <Check className="w-3.5 h-3.5" /> : <Save className="w-3.5 h-3.5" />}
                </button>
              </Tooltip>
            }
            className="w-48"
            variant="bordered"
          />
          <Tooltip content="Refresh Discussions">
            <Button
              size="sm"
              variant="flat"
              color="primary"
              isIconOnly
              onPress={reload}
              aria-label="Refresh Discussions"
            >
              <RefreshCw className="w-4 h-4" />
            </Button>
          </Tooltip>
          <Tooltip content="Create New Discussion">
            <Button
              color="primary"
              variant="shadow"
              size="sm"
              isIconOnly
              onPress={onOpen}
              aria-label="New Discussion"
              className="shadow-md shadow-primary/25"
            >
              <Plus className="w-4 h-4" />
            </Button>
          </Tooltip>
        </div>
      </div>

      {/* Category Filter Chips */}
      <div className="flex items-center gap-2 overflow-x-auto pb-1 scrollbar-none">
        {categories.map((cat) => (
          <Button
            key={cat}
            size="sm"
            variant={selectedFilter === cat ? 'solid' : 'flat'}
            color={selectedFilter === cat ? 'primary' : 'default'}
            onPress={() => setSelectedFilter(cat)}
            className={`text-xs font-semibold capitalize ${
              selectedFilter === cat ? 'text-white' : 'text-slate-300 bg-zinc-800/70 hover:text-white'
            }`}
          >
            {cat}
          </Button>
        ))}
      </div>

      {/* Discussions Grid */}
      {filteredIssues.length === 0 ? (
        <div className="text-center py-12 bg-[#18181b]/80 rounded-2xl border border-white/10">
          <MessageSquare className="w-10 h-10 text-slate-500 mx-auto mb-2 opacity-60" />
          <h4 className="text-base font-bold text-white">No discussions found</h4>
          <p className="text-xs text-slate-400 mt-1">Be the first to start a community discussion or question!</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {filteredIssues.map((issue) => {
            const isMine = issue.instance_id === myInstanceId;

            return (
              <Card
                key={issue.id}
                isPressable
                onPress={() => handleOpenDetail(issue)}
                className="bg-[#18181b]/95 border border-white/10 shadow-md backdrop-blur-md hover:border-primary/60 transition-all text-left group"
              >
                <CardBody className="p-5 space-y-3">
                  <div className="flex items-start justify-between gap-2">
                    <div className="space-y-1">
                      <div className="flex items-center gap-2">
                        <Chip size="sm" color="primary" variant="flat" className="text-[11px] font-semibold">
                          {issue.category}
                        </Chip>
                        <span className="text-xs text-slate-400 font-medium">by {issue.author}</span>
                        {isMine && (
                          <Chip size="sm" color="secondary" variant="flat" className="text-[10px] h-5">
                            You
                          </Chip>
                        )}
                      </div>
                      <h4 className="font-bold text-white text-base group-hover:text-primary transition-colors line-clamp-1">
                        {issue.title}
                      </h4>
                    </div>
                    <div className="flex items-center gap-1.5">
                      {isMine && (
                        <>
                          <Button
                            size="sm"
                            variant="light"
                            isIconOnly
                            className="text-slate-400 hover:text-white min-w-7 w-7 h-7"
                            onPress={() => handleOpenEdit({
                              type: 'issue',
                              id: issue.id,
                              title: issue.title,
                              content: issue.description,
                              imageUrl: issue.image_url
                            })}
                          >
                            <Edit3 className="w-3.5 h-3.5" />
                          </Button>
                          <Button
                            size="sm"
                            variant="light"
                            isIconOnly
                            color="danger"
                            className="text-danger-400 hover:text-danger min-w-7 w-7 h-7"
                            onPress={() => handleDeleteIssue(issue.id)}
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </Button>
                        </>
                      )}
                    </div>
                  </div>

                  <p className="text-xs text-slate-200 line-clamp-3 leading-relaxed font-normal">
                    {issue.description}
                  </p>

                  {issue.image_url && (
                    <ImagePreview
                      src={issue.image_url}
                      alt={issue.title}
                      className="aspect-video w-full max-h-48"
                      onExpand={handleOpenLightbox}
                    />
                  )}

                  <div className="flex items-center justify-between pt-2.5 border-t border-white/10 text-xs text-slate-300">
                    <div className="flex items-center gap-4">
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          toggleUpvote(issue.id);
                        }}
                        className={`flex items-center gap-1.5 font-medium transition-colors ${
                          issue.upvotedByMe ? 'text-primary font-bold' : 'text-slate-300 hover:text-white'
                        }`}
                      >
                        <ThumbsUp className="w-3.5 h-3.5" /> {issue.upvotes || 0}
                      </button>
                      <span className="flex items-center gap-1.5 text-slate-300">
                        <MessageCircle className="w-3.5 h-3.5 text-secondary" /> {issue.comments_count || 0}
                      </span>
                    </div>
                    <span className="text-slate-400 text-[11px]">{new Date(issue.created_at).toLocaleDateString()}</span>
                  </div>
                </CardBody>
              </Card>
            );
          })}
        </div>
      )}

      {/* Discussion Details & Replies Modal */}
      <Modal isOpen={isDetailOpen} onOpenChange={onDetailOpenChange} size="2xl" className="dark bg-[#18181b] text-white border border-white/15 shadow-2xl">
        <ModalContent>
          {(onClose) => {
            const isPostMine = activeIssue?.instance_id === myInstanceId;

            return (
              <>
                <ModalHeader className="flex flex-col gap-1 border-b border-white/10 p-5 pr-14">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      <Chip size="sm" color="primary" variant="flat" className="font-semibold text-xs">
                        {activeIssue?.category || 'General'}
                      </Chip>
                      <span className="text-xs text-slate-400 font-medium">by {activeIssue?.author}</span>
                      {isPostMine && (
                        <Chip size="sm" color="secondary" variant="flat" className="text-[10px] h-5">
                          You
                        </Chip>
                      )}
                    </div>
                    {isPostMine && (
                      <div className="flex items-center gap-2 mr-6">
                        <Button
                          size="sm"
                          variant="flat"
                          color="primary"
                          startContent={<Edit3 className="w-3.5 h-3.5" />}
                          onPress={() => {
                            if (activeIssue) {
                              handleOpenEdit({
                                type: 'issue',
                                id: activeIssue.id,
                                title: activeIssue.title,
                                content: activeIssue.description,
                                imageUrl: activeIssue.image_url
                              });
                            }
                          }}
                        >
                          Edit Post
                        </Button>
                        <Button
                          size="sm"
                          variant="flat"
                          color="danger"
                          startContent={<Trash2 className="w-3.5 h-3.5" />}
                          onPress={() => {
                            if (activeIssue) handleDeleteIssue(activeIssue.id);
                          }}
                        >
                          Delete
                        </Button>
                      </div>
                    )}
                  </div>
                  <h3 className="text-xl font-bold text-white mt-1 pr-6">{activeIssue?.title}</h3>
                </ModalHeader>

                <ModalBody className="p-5 space-y-4 max-h-[65vh] overflow-y-auto">
                  <div className="p-4 rounded-xl bg-zinc-900/90 border border-white/10 space-y-3">
                    <p className="text-sm text-slate-100 whitespace-pre-wrap leading-relaxed font-normal">
                      {activeIssue?.description}
                    </p>
                    {activeIssue?.image_url && (
                      <div className="pt-2">
                        <ImagePreview
                          src={activeIssue.image_url}
                          alt={activeIssue.title}
                          className="max-h-96 w-full object-contain"
                          onExpand={handleOpenLightbox}
                        />
                      </div>
                    )}
                  </div>

                  <div className="space-y-3 pt-2">
                    <h4 className="text-xs font-bold uppercase tracking-wider text-slate-400">
                      Replies ({replies.length})
                    </h4>

                    {loadingReplies ? (
                      <div className="py-6 flex justify-center">
                        <Spinner size="sm" label="Loading replies..." />
                      </div>
                    ) : replies.length === 0 ? (
                      <p className="text-xs text-slate-400 italic py-2">No replies yet. Be the first to join the discussion!</p>
                    ) : (
                      <div className="space-y-2.5">
                        {replies.map((reply) => {
                          const isReplyMine = reply.instance_id === myInstanceId;

                          return (
                            <div key={reply.id} className="p-3.5 rounded-xl bg-zinc-900/80 border border-white/10 text-xs space-y-1.5">
                              <div className="flex items-center justify-between">
                                <div className="flex items-center gap-2">
                                  <span className="font-bold text-white">{reply.username || reply.author}</span>
                                  {isReplyMine && (
                                    <Chip size="sm" color="secondary" variant="flat" className="text-[10px] h-4 px-1.5">
                                      You
                                    </Chip>
                                  )}
                                </div>
                                <div className="flex items-center gap-2">
                                  <span className="text-[11px] text-slate-400">{new Date(reply.created_at).toLocaleDateString()}</span>
                                  {isReplyMine && (
                                    <div className="flex items-center gap-1">
                                      <Button
                                        size="sm"
                                        variant="light"
                                        isIconOnly
                                        className="text-slate-400 hover:text-white min-w-6 w-6 h-6"
                                        onPress={() => handleOpenEdit({
                                          type: 'reply',
                                          id: reply.id,
                                          content: reply.content,
                                          imageUrl: reply.image_url
                                        })}
                                      >
                                        <Edit3 className="w-3 h-3" />
                                      </Button>
                                      <Button
                                        size="sm"
                                        variant="light"
                                        isIconOnly
                                        color="danger"
                                        className="text-danger-400 hover:text-danger min-w-6 w-6 h-6"
                                        onPress={() => handleDeleteReply(reply.id)}
                                      >
                                        <Trash2 className="w-3 h-3" />
                                      </Button>
                                    </div>
                                  )}
                                </div>
                              </div>
                              <p className="text-slate-100 leading-relaxed font-normal">{reply.content}</p>
                              {reply.image_url && (
                                <div className="pt-1">
                                  <ImagePreview
                                    src={reply.image_url}
                                    alt="Reply attachment"
                                    className="max-h-48 w-full object-contain"
                                    onExpand={handleOpenLightbox}
                                  />
                                </div>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    )}

                    {/* Reply Attachment Preview */}
                    {replyImagePreview && (
                      <div className="flex items-center gap-2 p-2 rounded-lg bg-zinc-900 border border-white/10 w-fit">
                        <img src={replyImagePreview} alt="" className="w-10 h-10 object-cover rounded" />
                        <span className="text-xs text-slate-300 truncate max-w-xs">{replyImageFile?.name}</span>
                        <Button
                          size="sm"
                          variant="light"
                          isIconOnly
                          className="min-w-6 w-6 h-6 text-slate-400 hover:text-white"
                          onPress={() => {
                            setReplyImageFile(null);
                            setReplyImagePreview(null);
                          }}
                        >
                          <X className="w-3.5 h-3.5" />
                        </Button>
                      </div>
                    )}

                    <div className="flex items-center gap-2 pt-2">
                      <input
                        type="file"
                        ref={replyFileInputRef}
                        accept="image/*"
                        onChange={handleReplyImageSelect}
                        className="hidden"
                      />
                      <Button
                        size="sm"
                        variant="flat"
                        isIconOnly
                        className="text-slate-300 hover:text-white"
                        onPress={() => replyFileInputRef.current?.click()}
                      >
                        <ImageIcon className="w-4 h-4" />
                      </Button>
                      <Input
                        size="sm"
                        placeholder="Write a reply..."
                        value={replyText}
                        onValueChange={setReplyText}
                        variant="bordered"
                        className="text-white"
                        onKeyDown={(e) => {
                          if (e.key === 'Enter' && !e.shiftKey) {
                            e.preventDefault();
                            handleSendReply();
                          }
                        }}
                      />
                      <Button
                        size="sm"
                        color="primary"
                        isLoading={submittingReply}
                        onPress={handleSendReply}
                        isIconOnly
                      >
                        <Send className="w-4 h-4" />
                      </Button>
                    </div>
                  </div>
                </ModalBody>

                <ModalFooter className="border-t border-white/10 p-4">
                  <Button color="default" variant="light" onPress={onClose} className="text-slate-300 hover:text-white">
                    Close
                  </Button>
                </ModalFooter>
              </>
            );
          }}
        </ModalContent>
      </Modal>

      {/* Edit Post / Reply Modal (Elevated z-index to appear on top of all modals) */}
      <Modal
        isOpen={isEditOpen}
        onOpenChange={onEditOpenChange}
        className="dark bg-[#18181b] text-white border border-white/20 shadow-2xl z-[99999]"
        classNames={{
          wrapper: 'z-[99999]',
          backdrop: 'z-[99998] bg-black/70 backdrop-blur-sm'
        }}
      >
        <ModalContent>
          {(onClose) => (
            <>
              <ModalHeader className="flex flex-col gap-1 p-5 border-b border-white/10">
                <span className="text-white text-lg font-bold">
                  {editTarget?.type === 'issue' ? 'Edit Discussion Post' : 'Edit Reply'}
                </span>
                <span className="text-xs text-slate-400 font-normal">
                  Update your {editTarget?.type === 'issue' ? 'post content' : 'comment message'}
                </span>
              </ModalHeader>
              <ModalBody className="space-y-4 p-5">
                {editTarget?.type === 'issue' && (
                  <Input
                    label="Title"
                    placeholder="Discussion title..."
                    value={editTitle}
                    onValueChange={setEditTitle}
                    variant="bordered"
                    autoFocus
                  />
                )}
                <Textarea
                  label={editTarget?.type === 'issue' ? 'Description' : 'Message'}
                  placeholder={editTarget?.type === 'issue' ? 'Edit your post description...' : 'Edit your reply...'}
                  value={editContent}
                  onValueChange={setEditContent}
                  minRows={4}
                  variant="bordered"
                  autoFocus={editTarget?.type !== 'issue'}
                />

                {/* Edit Screenshot Attachment */}
                <div className="space-y-2 pt-1">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-semibold text-slate-300">Screenshot Attachment</span>
                    <input
                      type="file"
                      ref={editFileInputRef}
                      accept="image/*"
                      onChange={handleEditImageSelect}
                      className="hidden"
                    />
                    <Button
                      size="sm"
                      variant="flat"
                      className="text-xs"
                      startContent={<ImageIcon className="w-3.5 h-3.5" />}
                      onPress={() => editFileInputRef.current?.click()}
                    >
                      {editImageUrl ? 'Replace Image' : 'Attach Image'}
                    </Button>
                  </div>
                  {editImageUrl && (
                    <div className="relative rounded-lg overflow-hidden border border-white/10 bg-black/40 p-1 w-fit">
                      <img src={editImageUrl} alt="" className="max-h-36 rounded object-contain" />
                      <Button
                        size="sm"
                        color="danger"
                        variant="flat"
                        isIconOnly
                        className="absolute top-2 right-2 min-w-6 w-6 h-6"
                        onPress={() => {
                          setEditImageUrl('');
                          setEditImageFile(null);
                        }}
                      >
                        <X className="w-3.5 h-3.5" />
                      </Button>
                    </div>
                  )}
                </div>
              </ModalBody>
              <ModalFooter className="border-t border-white/10 p-4">
                <Button color="default" variant="light" onPress={onClose} className="text-slate-300 hover:text-white">
                  Cancel
                </Button>
                <Button color="primary" isLoading={savingEdit} onPress={handleSaveEdit}>
                  Save Changes
                </Button>
              </ModalFooter>
            </>
          )}
        </ModalContent>
      </Modal>

      {/* New Discussion Modal */}
      <Modal isOpen={isOpen} onOpenChange={onOpenChange} className="dark bg-[#18181b] text-white border border-white/15">
        <ModalContent>
          {(onClose) => (
            <>
              <ModalHeader className="flex flex-col gap-1 p-5 border-b border-white/10">
                <span className="text-white text-lg font-bold">Start Community Discussion</span>
                <span className="text-xs text-slate-400 font-normal">Post feedback, request a feature, or ask a question</span>
              </ModalHeader>
              <ModalBody className="space-y-4 p-5">
                <Input
                  label="Title"
                  placeholder="e.g. Dreamcast widescreen hack recommendations"
                  value={newTitle}
                  onValueChange={setNewTitle}
                  variant="bordered"
                  autoFocus
                />
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-slate-300">Category</label>
                  <CustomSelect
                    value={newCategory}
                    onChange={(val) => setNewCategory(val)}
                    options={[
                      { value: 'Ideas', label: 'Ideas & Feedback' },
                      { value: 'Announcements', label: 'Announcements' },
                      { value: 'General', label: 'General' },
                      { value: 'Bugs', label: 'Bugs' },
                      { value: 'Q&A', label: 'Q&A / Help' },
                      { value: 'Show and Tell', label: 'Show and Tell' }
                    ]}
                    size="sm"
                  />
                </div>
                <Textarea
                  label="Description"
                  placeholder="Describe your suggestion, core settings, or question in detail..."
                  value={newDesc}
                  onValueChange={setNewDesc}
                  minRows={4}
                  variant="bordered"
                />

                {/* Image Attachment Upload */}
                <div className="space-y-2">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-semibold text-slate-300">Screenshot / Image (Optional)</span>
                    <input
                      type="file"
                      ref={newFileInputRef}
                      accept="image/*"
                      onChange={handleNewImageSelect}
                      className="hidden"
                    />
                    <Button
                      size="sm"
                      variant="flat"
                      className="text-xs text-slate-200"
                      startContent={<ImageIcon className="w-3.5 h-3.5" />}
                      onPress={() => newFileInputRef.current?.click()}
                    >
                      {newImagePreview ? 'Change Image' : 'Select Screenshot'}
                    </Button>
                  </div>
                  {newImagePreview && (
                    <div className="relative rounded-lg overflow-hidden border border-white/10 bg-black/40 p-1 w-fit">
                      <img src={newImagePreview} alt="" className="max-h-36 rounded object-contain" />
                      <Button
                        size="sm"
                        color="danger"
                        variant="flat"
                        isIconOnly
                        className="absolute top-2 right-2 min-w-6 w-6 h-6"
                        onPress={() => {
                          setNewImageFile(null);
                          setNewImagePreview(null);
                        }}
                      >
                        <X className="w-3.5 h-3.5" />
                      </Button>
                    </div>
                  )}
                </div>
              </ModalBody>
              <ModalFooter className="border-t border-white/10 p-4">
                <Button color="default" variant="light" onPress={onClose} className="text-slate-300 hover:text-white">
                  Cancel
                </Button>
                <Button color="primary" isLoading={creating} onPress={() => handleCreate(onClose)}>
                  Publish Post
                </Button>
              </ModalFooter>
            </>
          )}
        </ModalContent>
      </Modal>

      {/* Lightbox Modal for Full-Resolution Screenshots */}
      <Modal
        isOpen={isLightboxOpen}
        onOpenChange={onLightboxOpenChange}
        size="5xl"
        className="dark bg-black/95 text-white border border-white/20 z-[99999]"
        classNames={{
          wrapper: 'z-[99999]',
          backdrop: 'z-[99998] bg-black/90 backdrop-blur-md'
        }}
      >
        <ModalContent>
          {(onClose) => (
            <>
              <ModalHeader className="flex justify-between items-center p-4 border-b border-white/10">
                <span className="text-sm font-semibold text-slate-300">Screenshot Viewer</span>
                <div className="flex items-center gap-3 mr-6">
                  <a
                    href={lightboxImageUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-xs text-primary hover:underline flex items-center gap-1 font-medium"
                  >
                    Open Original <ExternalLink className="w-3.5 h-3.5" />
                  </a>
                  <Button size="sm" variant="light" isIconOnly onPress={onClose} className="text-slate-400 hover:text-white">
                    <X className="w-4 h-4" />
                  </Button>
                </div>
              </ModalHeader>
              <ModalBody className="p-4 flex items-center justify-center max-h-[80vh] overflow-auto">
                <img
                  src={lightboxImageUrl}
                  alt="Full resolution screenshot"
                  className="max-h-[75vh] w-auto max-w-full object-contain rounded-lg shadow-2xl"
                />
              </ModalBody>
            </>
          )}
        </ModalContent>
      </Modal>
    </div>
  );
};
