syntax enable
call plug#begin('~/.vim/plugged')
Plug 'ludovicchabant/vim-gutentags'
Plug 'junegunn/fzf.vim'
Plug 'rust-lang/rust.vim'
Plug 'racer-rust/vim-racer'
Plug 'jeffkreeftmeijer/vim-numbertoggle'
Plug 'vim-syntastic/syntastic'
Plug 'chriskempson/base16-vim'
Plug 'vim-airline/vim-airline'
Plug 'scrooloose/nerdcommenter'
Plug 'cespare/vim-toml'
Plug 'uarun/vim-protobuf'
Plug 'PProvost/vim-ps1'
Plug 'ntpeters/vim-better-whitespace'
Plug 'Chiel92/vim-autoformat'
Plug 'editorconfig/editorconfig-vim'
call plug#end()
set hidden
set laststatus=2
set t_Co=256
set number
set hlsearch
set colorcolumn=100
set noshowmode
set rtp+=/usr/local/bin/fzf
let g:indentLine_enabled=1
let g:indentLine_char='¦'
let g:better_whitespace_eabled=1
let g:strip_whitespace_on_save=1
let g:autoformat_autoindent=0
let g:autoformat_retab=0
let g:autoformat_remove_trailing_spaces=0
let g:syntastic_always_populate_loc_list=1
let g:syntastic_auto_loc_list=1
let g:syntastic_check_on_open=1
let g:syntastic_check_on_wq=0
let base16colorspace=256
let mapleader=","
let maplocalleader=","
let NERDSpaceDelims=1
autocmd FileType javascript set tabstop=2|set shiftwidth=2|set expandtab
autocmd FileType cs set tabstop=4|set shiftwidth=4|set expandtab
colorscheme base16-default-dark
nnoremap <silent> <C-l> :nohlsearch<CR><C-l>
map <C-p> :Files<CR>
map <leader>t :GFiles<CR>
map <leader>b :Buffers<CR>
map <leader>j :BTags<CR>
map <leader>J :Tags<CR>
map <leader>r :RunTests<CR>
map <leader>s :Kibit<CR>
map <leader>c :Require<CR>
au BufWrite * :Autoformat

