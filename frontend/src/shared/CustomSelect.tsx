import React, { useState, useRef, useEffect } from 'react';
import { ChevronDown, Check } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';

export interface SelectOption {
  value: string;
  label: string;
  description?: string;
  icon?: React.ReactNode;
}

export interface CustomSelectProps {
  value?: string | number | null;
  onChange: (val: string) => void;
  options: (SelectOption | { id: string | number; label: string })[];
  placeholder?: string;
  label?: string;
  className?: string;
  size?: 'sm' | 'md';
  disabled?: boolean;
  includeDefault?: boolean;
  defaultLabel?: string;
  dropUp?: boolean;
  id?: string;
}

export const CustomSelect: React.FC<CustomSelectProps> = ({
  value,
  onChange,
  options,
  placeholder = 'Select an option...',
  label,
  className = '',
  size = 'md',
  disabled = false,
  includeDefault = false,
  defaultLabel = 'Default (Global Setting)',
  dropUp,
  id
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const [openUpwards, setOpenUpwards] = useState(Boolean(dropUp));
  const containerRef = useRef<HTMLDivElement>(null);

  // Normalize options list
  const normalizedOptions: SelectOption[] = [];
  if (includeDefault) {
    normalizedOptions.push({ value: '', label: defaultLabel });
  }
  options.forEach(opt => {
    if ('id' in opt) {
      normalizedOptions.push({
        value: String(opt.id),
        label: opt.label
      });
    } else {
      normalizedOptions.push(opt);
    }
  });

  const rawStr = String(value ?? '').trim();
  let selectedOption = normalizedOptions.find(o => {
    if (o.value === rawStr) return true;
    const optVal = String(o.value ?? '').trim().toLowerCase();
    const targetVal = rawStr.toLowerCase();
    if (optVal === targetVal) return true;

    // Numeric normalization (e.g. "1.0" === "1" or 1 === 1.0)
    const numOpt = parseFloat(optVal);
    const numTarget = parseFloat(targetVal);
    if (!isNaN(numOpt) && !isNaN(numTarget) && numOpt === numTarget) return true;

    // Shader .glslp normalization
    if (optVal.replace(/\.glslp$/i, '') === targetVal.replace(/\.glslp$/i, '')) return true;

    // Scale normalization
    if ((optVal === 'fit' && targetVal === 'aspect') || (optVal === '1' && targetVal === 'native')) return true;

    // Boolean normalization
    if ((optVal === '1' && targetVal === 'true') || (optVal === '0' && targetVal === 'false')) return true;

    return false;
  });

  // If no option matched or value is empty, auto-select option with "(Default)" or "(default)"
  if (!selectedOption && normalizedOptions.length > 0 && (!rawStr || rawStr === 'default')) {
    const defaultOpt = normalizedOptions.find(o => o.label.toLowerCase().includes('(default)'));
    if (defaultOpt) {
      selectedOption = defaultOpt;
    }
  }

  // Close on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent | TouchEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    };

    if (isOpen) {
      document.addEventListener('mousedown', handleClickOutside);
      document.addEventListener('touchstart', handleClickOutside);
    }
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('touchstart', handleClickOutside);
    };
  }, [isOpen]);

  // Handle escape key
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isOpen) {
        setIsOpen(false);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen]);

  const toggleDropdown = () => {
    if (disabled) return;
    if (!isOpen && containerRef.current) {
      if (dropUp !== undefined) {
        setOpenUpwards(dropUp);
      } else {
        const rect = containerRef.current.getBoundingClientRect();
        const spaceBelow = window.innerHeight - rect.bottom;
        setOpenUpwards(spaceBelow < 220 && rect.top > 180);
      }
    }
    setIsOpen(!isOpen);
  };

  const handleSelect = (val: string) => {
    onChange(val);
    setIsOpen(false);
  };

  const sizeClasses = size === 'sm'
    ? 'px-3 py-1.5 text-xs min-h-[34px]'
    : 'px-3.5 py-2 text-xs sm:text-sm min-h-[38px]';

  return (
    <div className={`relative w-full ${className}`} ref={containerRef} id={id}>
      {label && (
        <label className="block text-xs font-semibold text-white mb-1.5">
          {label}
        </label>
      )}

      {/* Trigger Button */}
      <button
        type="button"
        onClick={toggleDropdown}
        disabled={disabled}
        className={`w-full flex items-center justify-between gap-2 rounded-xl bg-[#16161a] hover:bg-[#1f1f24] border transition-all duration-150 text-left shadow-md ${sizeClasses} ${
          isOpen
            ? 'border-white/50 ring-1 ring-white/25'
            : 'border-white/15 hover:border-white/30'
        } ${disabled ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
      >
        <div className="flex items-center gap-2 min-w-0 flex-1 truncate">
          {selectedOption?.icon && (
            <span className="shrink-0 text-white">{selectedOption.icon}</span>
          )}
          <span className={`truncate ${selectedOption ? 'text-white font-semibold' : 'text-neutral-400'}`}>
            {selectedOption ? selectedOption.label : placeholder}
          </span>
        </div>

        <ChevronDown
          className={`w-4 h-4 text-neutral-400 shrink-0 transition-transform duration-200 ${
            isOpen ? 'rotate-180 text-white' : ''
          }`}
        />
      </button>

      {/* Dropdown Menu Popup (100% Solid Opaque Background) */}
      <AnimatePresence>
        {isOpen && (
          <motion.div
            initial={{ opacity: 0, y: openUpwards ? 4 : -4, scale: 0.99 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: openUpwards ? 4 : -4, scale: 0.99 }}
            transition={{ duration: 0.12, ease: 'easeOut' }}
            className={`absolute left-0 right-0 z-50 rounded-xl bg-[#141418] border border-white/15 shadow-2xl overflow-hidden max-h-64 overflow-y-auto py-1.5 ${
              openUpwards ? 'bottom-full mb-1.5' : 'top-full mt-1.5'
            }`}
            style={{
              backgroundColor: '#141418',
              boxShadow: '0 20px 40px -8px rgba(0, 0, 0, 0.95), 0 0 0 1px rgba(255, 255, 255, 0.12)'
            }}
            role="listbox"
          >
            {normalizedOptions.length === 0 ? (
              <div className="px-3.5 py-2.5 text-xs text-neutral-400 text-center">
                No options available
              </div>
            ) : (
              normalizedOptions.map(opt => {
                const isSelected = selectedOption ? opt.value === selectedOption.value : opt.value === rawStr;
                return (
                  <button
                    key={opt.value || '__empty__'}
                    type="button"
                    onClick={() => handleSelect(opt.value)}
                    className={`w-full flex items-center justify-between gap-2 px-3.5 py-2 text-xs text-left transition-colors cursor-pointer group ${
                      isSelected
                        ? 'bg-white/10 text-white font-bold border-l-3 border-white pl-3'
                        : 'text-neutral-300 hover:bg-white/10 hover:text-white'
                    }`}
                    role="option"
                    aria-selected={isSelected}
                  >
                    <div className="flex items-center gap-2 min-w-0 flex-1 truncate">
                      {opt.icon && (
                        <span className="shrink-0 text-neutral-400 group-hover:text-white transition-colors">
                          {opt.icon}
                        </span>
                      )}
                      <div className="truncate">
                        <span className="block truncate text-white">{opt.label}</span>
                        {opt.description && (
                          <span className="block text-[10px] text-neutral-400 font-normal truncate mt-0.5">
                            {opt.description}
                          </span>
                        )}
                      </div>
                    </div>

                    {isSelected && (
                      <Check className="w-4 h-4 text-white shrink-0 ml-2" />
                    )}
                  </button>
                );
              })
            )}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
};
