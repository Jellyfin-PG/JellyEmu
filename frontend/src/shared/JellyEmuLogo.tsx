import React from 'react';

interface JellyEmuLogoProps extends React.SVGProps<SVGSVGElement> {
  className?: string;
  size?: number;
}

export const JellyEmuLogo: React.FC<JellyEmuLogoProps> = ({
  className = 'w-7 h-7',
  size,
  ...props
}) => {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      width={size}
      height={size}
      {...props}
    >
      <path d="M12 2L3 18a2 2 0 0 0 1.7 3h14.6a2 2 0 0 0 1.7-3L12 2z" />
      <path d="M7.5 15.5h3M9 14v3" strokeWidth="1.2" />
      <circle cx="15.5" cy="14.5" r="0.8" fill="currentColor" stroke="none" />
      <circle cx="17" cy="16.5" r="0.8" fill="currentColor" stroke="none" />
    </svg>
  );
};
