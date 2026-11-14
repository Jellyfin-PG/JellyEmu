export interface FeedProvider {
  name: string;
  domain: string;
  url: string;
  isEnabled: boolean;
}

export interface MarketplaceRom {
  title: string;
  system: string;
  providerName: string;
  detailUrl: string;
  thumbnailUrl: string;
  region?: string | null;
  version?: string | null;
  languages?: string | null;
  extraFlags?: string | null;
  hasManual?: boolean;
}

export interface DownloadResponse {
  success: boolean;
  filename?: string;
  message?: string;
}
