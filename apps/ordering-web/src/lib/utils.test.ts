import { describe, expect, it } from 'vitest'
import { cn } from './utils'

describe('cn', () => {
  it('joins class strings', () => {
    expect(cn('foo', 'bar')).toBe('foo bar')
  })

  it('dedupes conflicting tailwind utilities via twMerge', () => {
    expect(cn('p-4', 'p-2')).toBe('p-2')
  })

  it('drops nullish values', () => {
    expect(cn('foo', null, undefined, 'baz')).toBe('foo baz')
  })
})
